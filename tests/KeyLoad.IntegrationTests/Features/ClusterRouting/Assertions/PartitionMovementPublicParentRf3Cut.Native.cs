using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static partial class PartitionMovementPublicParentRf3Cut
{
    internal static Task<PartitionMovementPublicParentRf3NativeCut[]> StopAndReadAsync(
        TwoRf3MembershipWave wave, PartitionMoveRequest request, CancellationToken cancellationToken)
        => StopAndReadAsync(wave, request, Guid.Empty, cancellationToken);

    internal static async Task<PartitionMovementPublicParentRf3NativeCut[]> StopAndReadAsync(
        TwoRf3MembershipWave wave, PartitionMoveRequest request, Guid originalPhaseId,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.KillAsync(node,
                "public-parent-joined-cold-original-cut", cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "replica", "owner.lock"));
        }
        return TwoRf3MembershipProtocol.Nodes.Select(node => ReadStopped(wave, node, request, originalPhaseId)).ToArray();
    }

    internal static async Task RestartAsync(TwoRf3MembershipWave wave, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.RemoteRuntime.RestartAsync(node,
                cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static Task RequireCompactedAsync(PartitionMovementPublicParentRf3NativeCut[] cuts)
        => PartitionMovementPublicParentRf3CompactionAssertions.RequireAsync(cuts);

    internal static PartitionMovementPublicParentRf3NativeCut ReadStopped(TwoRf3MembershipWave wave,
        string node, PartitionMoveRequest request, Guid originalPhaseId)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        PartitionMovementPublicParentRf3NativeCut? result = null;
        ServerFailureObserver.Observe(() =>
        {
            store = new(new(Path.Combine(wave.OwnedDataRoot, node, "database")),
                IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution());
            result = store.Read(view =>
            {
                var limits = IntegrationExecutionOptions.DatabaseLimits().Value;
                var rows = view.Scan([], limits.MaxScanRecords);
                if (rows.HasMore)
                { throw new InvalidOperationException("The complete parent raw cut exceeded its native bound."); }
                var header = PartitionMoveParentStorage.Header(view, request.Partition, request.MoveId, limits.MaxBatchBytes);
                var pending = header?.PendingOriginalPhaseCommandId is { } id
                    ? PartitionMoveParentStorage.Phase(view, request.Partition, request.MoveId, id, limits.MaxBatchBytes) : null;
                var last = header is { LastOriginalPhaseCommandId: var lastId } && lastId != Guid.Empty
                    ? PartitionMoveParentStorage.Phase(view, request.Partition, request.MoveId, lastId, limits.MaxBatchBytes) : null;
                var original = originalPhaseId == Guid.Empty ? null
                    : PartitionMoveParentStorage.Phase(view, request.Partition, request.MoveId, originalPhaseId, limits.MaxBatchBytes);
                var cancellation = originalPhaseId == Guid.Empty ? null
                    : PartitionMoveRetireCancellationStorage.Read(view, request.Partition, request.MoveId,
                        originalPhaseId, limits.MaxBatchBytes);
                var cancellationId = cancellation?.CancellationCommandId ?? original?.RetireCancellationAttempt?.CancellationCommandId;
                var cancellationPrincipal = cancellation?.CancellationPrincipalId ?? header?.OperatorPrincipalId;
                var nativeResult = cancellationId is { } commandId && cancellationPrincipal is { } principal
                    ? CommandOutcomeKeyResolver.Select(view, principal, commandId,
                        new(CommandOutcomeScopeKind.Partition, request.Partition)).Outcome?.Result : null;
                var actualGrant = original?.OriginalGrant is { } retainedGrant
                    ? PartitionMoveGrantStorage.Read(view, request.Partition, retainedGrant.GrantId, limits.MaxBatchBytes) : null;
                var moveGrants = PartitionMoveGrantStorage.Outstanding(view,
                    PartitionMoveGrantStorage.MoveCountKey(request.Partition, request.MoveId));
                var operatorId = original?.OriginalGrant?.OperatorPrincipalId ?? header?.OperatorPrincipalId;
                long? principalGrants = operatorId is { } actualOperator
                    ? PartitionMoveGrantStorage.Outstanding(view, actualOperator) : null;
                var databaseGrants = PartitionMoveGrantStorage.Outstanding(view,
                    PartitionMoveGrantStorage.DatabaseKey(request.Partition.TenantId, request.Partition.DatabaseId));
                var phasePrefix = PartitionMoveParentKeys.PhasePrefix(request.Partition, request.MoveId);
                var settledPages = rows.Records.Where(row => row.Key.Span.StartsWith(phasePrefix))
                    .Select(row => NativeSerialization.Deserialize<PartitionMoveParentPhase>(row.Value.Span))
                    .Where(row => row.Stage == PartitionMovePeerStage.StagePage && row.OriginalResult is not null)
                    .Select(row => PartitionMoveParentStorage.Phase(view, request.Partition, request.MoveId,
                        row.OriginalPhaseCommandId, limits.MaxBatchBytes)!).ToArray();
                var issued = originalPhaseId == Guid.Empty ? null
                    : PartitionMoveReceiverIssuanceStorage.Read(view, request.Partition, request.MoveId,
                        originalPhaseId, limits.MaxBatchBytes);
                var originalNativeResult = issued is null ? null
                    : CommandOutcomeKeyResolver.Select(view, issued.ReceiverPrincipalId, originalPhaseId,
                        new(CommandOutcomeScopeKind.Partition, request.Partition)).Outcome?.Result;
                return new PartitionMovementPublicParentRf3NativeCut(node, store.Position, rows.Records.Select(row => Convert.ToHexString(row.Key.Span)
                    + ":" + Convert.ToHexString(row.Value.Span)).ToArray(), header, pending, last, original, cancellation, nativeResult, originalNativeResult, issued, settledPages, actualGrant, moveGrants, principalGrants, databaseGrants);
            });
        }, failures);
        if (store is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The actual stopped parent cut is absent.");
    }

}

/// <summary>Original native store bytes and validated retained parent rows after all six owned processes exited.</summary>
internal sealed record PartitionMovementPublicParentRf3NativeCut(string Node, long StorePosition, string[] Rows,
    PartitionMoveParentHeader? Header, PartitionMoveParentPhase? Pending, PartitionMoveParentPhase? LastIssued,
    PartitionMoveParentPhase? OriginalPhase, PartitionMoveExpiredRetireCancellation? RetireCancellation,
    OperationResult? NativeCancellationResult, OperationResult? OriginalNativeResult,
    PartitionMoveReceiverIssuance? OriginalReceiverIssuance, PartitionMoveParentPhase[] SettledStagePages,
    PartitionMovePhaseGrant? OriginalControlGrant, long OutstandingMoveGrants,
    long? OutstandingPrincipalGrants, long OutstandingDatabaseGrants);
