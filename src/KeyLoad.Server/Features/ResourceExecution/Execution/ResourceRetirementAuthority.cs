using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Resumes durable exact-lifetime retirement without rolling back accepted phases on cancellation.</summary>
internal sealed class ResourceRetirementAuthority(PartitionHost partition, ResourceOwnerInventoryNativeClient client,
    ResourceOwnerInventoryAuthority inventory, IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
    TimeProvider clock)
{
    private const int NoPageSize = 0;
    private readonly ResourceRetirementReads reads = new(client);
    private readonly ResourceRetirementPhaseExecution phases = new(client);

    internal async Task<ResourceRetirementStateV1> ExecuteAsync(string sourcePrincipalId, long sourcePolicyEpoch,
        ResourceRetirementRequestV1 request, int pageSize, DateTimeOffset originalExpiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        token.ThrowIfCancellationRequested();
        if (pageSize <= NoPageSize || pageSize > limits.Value.MaxResults)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }
        if (originalExpiry <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.ConstrainLifetime(originalExpiry);
        using var cancellation = work.EnterStageCancellation(token);
        var source = PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition);
        var state = await LoadOrBeginAsync(request, source, sourcePrincipalId, sourcePolicyEpoch,
            originalExpiry, work, metadata, token).ConfigureAwait(false);
        if (state.Stage == ResourceRetirementStage.BarrierSealed)
        {
            work.CheckResult(state);
            return state;
        }
        state = await new ResourceRetirementFencing(reads, phases).ExecuteAsync(state, source,
            sourcePrincipalId, sourcePolicyEpoch, originalExpiry, work, metadata, token).ConfigureAwait(false);
        var refreshed = await ReadStateAsync(request, source, sourcePrincipalId, sourcePolicyEpoch,
            originalExpiry, work, metadata, token).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementReceiptValidation.RequireState(refreshed, request, source);
        if (!ResourceRetirementReceiptValidation.Same(state.Control, refreshed.Control))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (refreshed.Stage == ResourceRetirementStage.BarrierSealed)
        {
            work.CheckResult(refreshed);
            return refreshed;
        }
        return await new ResourceRetirementFinalization(reads, phases, inventory, limits, clock).ExecuteAsync(
            refreshed, source, pageSize, originalExpiry, sourcePrincipalId, sourcePolicyEpoch, work, metadata, token)
            .ConfigureAwait(false);
    }

    private async Task<ResourceRetirementStateV1> LoadOrBeginAsync(ResourceRetirementRequestV1 request,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var state = await ReadStateAsync(request, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        if (state is not null)
        {
            ResourceRetirementReceiptValidation.RequireState(state, request, source);
            return state;
        }
        return await new ResourceRetirementPreparing(reads, phases, limits, clock).ExecuteAsync(request, source,
            principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
    }

    private async Task<ResourceRetirementStateV1?> ReadStateAsync(ResourceRetirementRequestV1 request,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => (await reads.ExecuteAsync(ResourceRetirementReadOperation.State, request, null, source, Guid.Empty, NoPageSize,
            expiry, principalId, epoch, work, metadata, token).ConfigureAwait(false)).State;

}
