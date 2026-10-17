using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PlacementIntentV1 AcceptPlacementIntentReceipt(IKeyValueView view, PlacementIntentV1 original,
        PlacementIntentPhaseRequestV1 request)
    {
        if (original.CompletedMoves >= original.Plan.Moves.Length)
        {
            throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale);
        }
        var move = original.Plan.Moves[original.CompletedMoves];
        var header = PartitionMoveParentStorage.Header(view, move.Partition, move.MoveId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, TenantPlacementPolicyProtocol.Missing);
        var receipt = request.OriginalMoveReceipt
            ?? throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid);
        if (header.OperatorPrincipalId != original.PrincipalId || header.TerminalResult is not { } actual
            || actual.Phase != PartitionMovePhase.Retired || actual.MoveId != move.MoveId
            || actual.Partition != move.Partition || actual.DestinationOwner.PhysicalShardId != move.DestinationPhysicalShardId
            || !NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(receipt)))
        {
            throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale);
        }
        var completed = checked(original.CompletedMoves + TenantPlacementPolicyProtocol.RevisionStep);
        var status = completed == original.Plan.Moves.Length
            ? PlacementIntentStatus.Completed : PlacementIntentStatus.Paused;
        if (request.TargetStatus != status)
        {
            throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale);
        }
        return original with
        {
            CompletedMoves = checked((int)completed),
            Status = status,
            Receipts = original.Receipts.Add(actual)
        };
    }
}
