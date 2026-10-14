using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentObservationProof
{
    internal static PartitionMovePhaseResult TerminalCleanup(PartitionMoveParentPhase original,
        PartitionMovePeerStage expected)
    {
        var actual = Observed(original, expected);
        if (original.OriginalPhase is null || original.OriginalGrant is null || actual.Cleanup?.Completion is null
            || !NativeSerialization.Serialize(actual.Cleanup.Completion).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(actual.Journal)))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        }
        return actual;
    }

    internal static PartitionMovePhaseResult Observed(PartitionMoveParentPhase original, PartitionMovePeerStage expected)
    {
        if (original.Stage != expected || original.OriginalResult is null
            || original.ObservationCheckpointReceipt is null || original.OriginalResult.Error is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        }
        var actual = original.OriginalResult.Get<PartitionMovePhaseResult>();
        if (actual.Stage != expected || actual.MoveId != original.MoveId
            || actual.Journal.CommandId != original.OriginalPhaseCommandId)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        }
        return actual;
    }
}
