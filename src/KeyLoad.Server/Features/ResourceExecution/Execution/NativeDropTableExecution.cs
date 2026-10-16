using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Uses the original native owners to complete retirement, work join, cleanup and catalog finalization.</summary>
internal sealed class NativeDropTableExecution(OrleansNode node, PartitionHostApplicationOwner owner,
    ServerRuntimeOptions options, TimeProvider clock) : INativeDropTableExecution
{
    private const int MetadataHeadroomDivisor = 2;

    public async Task<OperationResult> ExecuteAsync(Guid requestId, Guid commandId, string principalId,
        DropTableRequest request, DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        node.RequireLocalRuntimePublished();
        var partition = owner.Borrow();
        var client = node.ResourceInventoryRuntime?.Client
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        if (requestId == Guid.Empty || commandId == Guid.Empty || request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }
        if (originalExpiry <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
        }
        var work = new ReadExecutionBudget(partition.Database.OperationLimitsOptions, clock, cancellationToken);
        work.ConstrainLifetime(originalExpiry);
        var bytes = work.RemainingReadGrantBytes / MetadataHeadroomDivisor;
        var records = work.RemainingReadGrantRecords / MetadataHeadroomDivisor;
        if (bytes <= ResourceRetirementReadContract.NoReadBytes || records <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }

        var metadata = work.CreateReadGrant(bytes, records);
        var failures = new List<Exception>();
        OperationResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            result = await ExecuteCoreAsync(partition, client, principalId, commandId, request, originalExpiry,
                work, metadata, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(metadata), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
    }

    private async Task<OperationResult> ExecuteCoreAsync(PartitionHost partition, ResourceOwnerInventoryNativeClient client,
        string principalId, Guid commandId, DropTableRequest request, DateTimeOffset originalExpiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken cancellationToken)
    {
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        var principal = partition.Database.Store.Read(raw => partition.Database.Principal(work.CreateView(raw, metadata),
            principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var source = PhysicalOwnerConfiguredTuples.Control(options.Node.Value, partition.Configuration);
        var context = new NativeDropTableContext(partition, client, options, clock, source, principal.Id,
            principal.PolicyEpoch, originalExpiry, work, metadata, cancellationToken);
        var lookup = new ResourceRetirementDropParentLookupV1(ResourceRetirementNativeProtocol.Version, commandId, request);
        var failures = new List<Exception>();
        OperationResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            result = await NativeDropTableFlow.ExecuteAsync(context, lookup).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (failures.Count != NativeDropTableContext.NoFailures)
        {
            if (context.WorkExecution is { } original)
            {
                failures.InsertRange(NativeDropTableContext.NoFailures, original.OriginalSubmissionFailures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return result ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
    }
}
