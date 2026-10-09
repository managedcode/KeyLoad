using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Reads actual stopped persisted admin state; the structural threshold is supporting evidence, not a pre-admission history.</summary>
internal static class PartitionMovementParentCapacityRf3Snapshot
{
    private const string Administrator = PartitionMovementPublicParentRf3Administrator.PrincipalId;
    private const long BoundaryStep = 1;
    private const int MidpointDivisor = 2;

    internal static PartitionMoveParentState Read(TwoRf3MembershipWave wave, string node,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveRequest request, int actualLimit,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        PartitionMoveParentState? state = null;
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            var limits = IntegrationExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = actualLimit });
            var database = new DatabaseEngine(store, new AuthorizationPolicy(), limits, IntegrationExecutionOptions.DueWork(),
                IntegrationExecutionOptions.EventSource(), IntegrationExecutionOptions.Messaging(), IntegrationExecutionOptions.GraphExecution(),
                IntegrationExecutionOptions.ChangeFeedExecution(), IntegrationExecutionOptions.BlobExecution(),
                IntegrationExecutionOptions.NativeClaimsExecution(), IntegrationExecutionOptions.TimeSeriesExecution(), IntegrationExecutionOptions.MovementCheckpoints(),
                UnavailablePartitionMovementCheckpointVerifier.Instance, physicalOwner: seed.Directory.ControlOwner);
            var work = new ReadExecutionBudget(limits, TimeProvider.System, cancellationToken);
            var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
            var header = store.Read(view => PartitionMoveParentStorage.Header(view, request.Partition,
                request.MoveId, actualLimit)) ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            state = database.ReadPartitionMovementParentState(Administrator, request,
                header.OriginalCapturePhaseCommandId ?? Guid.Empty, work, grant);
        }, failures);
        if (store is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return state ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
    }

    internal static PartitionMovePhaseGrant? ReadGrant(TwoRf3MembershipWave wave, string node,
        PartitionMoveRequest request, Guid actualGrantId, int actualLimit)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        PartitionMovePhaseGrant? grant = null;
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            grant = store.Read(view => PartitionMoveGrantStorage.Read(view, request.Partition, actualGrantId, actualLimit));
        }, failures);
        if (store is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return grant;
    }

    internal static async Task<long> RequireObservedBoundaryAsync(PartitionMoveParentState actual,
        string actualClusterId, CancellationToken cancellationToken)
    {
        var pending = actual.Pending ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(pending.Stage).IsEqualTo(PartitionMovePeerStage.ControlAuthorize);
        var issued = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(pending.OriginalPhase!.Body.Span);
        await Assert.That(issued.Phase.Stage).IsEqualTo(PartitionMovePeerStage.StagePage);
        await Assert.That(pending.OriginalGrant).IsNull();
        var resources = actual.Selected!.OriginalDescriptor!.Resources;
        var lower = BoundaryStep;
        var upper = (long)int.MaxValue;
        while (lower < upper)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var middle = lower + (upper - lower) / MidpointDivisor;
            if (Accepts(middle))
            { upper = middle; }
            else
            { lower = checked(middle + BoundaryStep); }
        }
        PartitionMovementParentCaptureLimits.RequirePlannedStageCapacity(actual, issued.Phase,
            issued.ReceiverOwner, resources, actualClusterId, lower);
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() =>
        {
            PartitionMovementParentCaptureLimits.RequirePlannedStageCapacity(actual, issued.Phase,
                issued.ReceiverOwner, resources, actualClusterId, lower - BoundaryStep);
            return Task.CompletedTask;
        });
        await Assert.That(denied?.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        return lower;

        bool Accepts(long maximum)
        {
            try
            {
                PartitionMovementParentCaptureLimits.RequirePlannedStageCapacity(actual, issued.Phase,
                    issued.ReceiverOwner, resources, actualClusterId, maximum);
                return true;
            }
            catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded) { return false; }
        }
    }
}
