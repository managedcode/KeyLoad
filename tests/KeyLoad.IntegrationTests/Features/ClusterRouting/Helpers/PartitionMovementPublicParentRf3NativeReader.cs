using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Reads the complete bounded native parent image from the caller-owned stopped store.</summary>
internal static class PartitionMovementPublicParentRf3NativeReader
{
    internal static PartitionMovementPublicParentRf3NativeCut ReadView(string node, PartitionMoveRequest request, Guid originalPhaseId, ZoneTreeStore store)
    {
        return store.Read(view =>
        {
            var limits = IntegrationExecutionOptions.DatabaseLimits().Value;
            var rows = view.Scan([], limits.MaxScanRecords);
            if (rows.HasMore)
            {
                throw new InvalidOperationException("The complete parent raw cut exceeded its native bound.");
            }
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
    }
}
