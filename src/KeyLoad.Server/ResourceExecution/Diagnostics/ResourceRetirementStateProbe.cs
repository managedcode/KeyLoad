using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Reads actual durable retirement state without admitting any retirement phase.</summary>
internal sealed class ResourceRetirementStateProbe(PartitionHost partition, ResourceOwnerInventoryNativeClient client,
    IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    private const int NoPageSize = 0;
    internal async Task<ResourceRetirementStateV1> ReadAsync(string sourcePrincipalId, long sourcePolicyEpoch,
        ResourceRetirementRequestV1 request, DateTimeOffset originalExpiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        token.ThrowIfCancellationRequested();
        ResourceRetirementValidation.Request(request, limits.Value);
        if (originalExpiry <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.ConstrainLifetime(originalExpiry);
        using var cancellation = work.EnterStageCancellation(token);
        var source = PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition);
        var reply = await new ResourceRetirementReads(client).ExecuteAsync(ResourceRetirementReadOperation.State,
            request, null, source, Guid.Empty, NoPageSize, originalExpiry, sourcePrincipalId, sourcePolicyEpoch, work, metadata, token)
            .ConfigureAwait(false);
        var state = reply.State ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementReceiptValidation.RequireState(state, request, source);
        work.CheckResult(state);
        token.ThrowIfCancellationRequested();
        return state;
    }

    internal async Task<ResourceRetirementPreparationStateV1> ReadPreparationAsync(string sourcePrincipalId,
        long sourcePolicyEpoch, ResourceRetirementRequestV1 request, DateTimeOffset originalExpiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        token.ThrowIfCancellationRequested();
        ResourceRetirementValidation.Request(request, limits.Value);
        if (originalExpiry <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.ConstrainLifetime(originalExpiry);
        using var cancellation = work.EnterStageCancellation(token);
        var source = PhysicalOwnerConfiguredTuples.Control(nodeOptions.Value, partition);
        var reply = await new ResourceRetirementReads(client).ExecuteAsync(ResourceRetirementReadOperation.Preparation,
            request, null, source, Guid.Empty, NoPageSize, originalExpiry, sourcePrincipalId, sourcePolicyEpoch, work, metadata, token)
            .ConfigureAwait(false);
        var state = reply.Preparation ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementPreparationStatuses.RequireState(state, request, source);
        work.CheckResult(state);
        token.ThrowIfCancellationRequested();
        return state;
    }

}
