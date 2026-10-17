using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Admits actual persisted movement snapshots without requiring a synthetic final progress phase.</summary>
internal sealed class PartitionMovementStreamShape(Guid requestId)
{
    internal const string PhaseCompleted = "The controlled partition movement phase completed.";

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int count)
    {
        if (chunk.Kind != CqrsStreamChunkKind.Progress || chunk.Sequence != count
            || chunk.ProgressResult is not { IsSuccess: true, Value: not null } result
            || result.Problem is not null || result.Value is not { } progress
            || progress.RequestId != requestId || progress.AnnPhase is not null || progress.TextPhase is not null
            || progress.OnlineTextPhase is not null || progress.EventFeedTail is not null || progress.PlacementIntent is not null
            || progress.MovePhase is not (PartitionMovePhase.Prepared or PartitionMovePhase.Fenced
                or PartitionMovePhase.Captured or PartitionMovePhase.Transferring or PartitionMovePhase.Installed
                or PartitionMovePhase.Published or PartitionMovePhase.Aborting)
            || chunk.Final is not null || chunk.EventId is not null || chunk.Message != PhaseCompleted
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
