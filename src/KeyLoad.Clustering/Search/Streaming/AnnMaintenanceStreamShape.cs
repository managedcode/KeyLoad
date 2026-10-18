using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Validates the ordered ANN phases of one verified signed native maintenance operation.</summary>
internal sealed class AnnMaintenanceStreamShape(Guid requestId)
{
    private AnnMaintenancePhase? phase;
    internal bool Completed => phase == AnnMaintenancePhase.Completed;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int count)
    {
        if (chunk.Kind != CqrsStreamChunkKind.Progress || chunk.Sequence != count
            || chunk.ProgressResult is not { IsSuccess: true, Value: not null } result
            || result.Problem is not null || result.Value is not { } progress
            || progress.RequestId != requestId || progress.TextPhase is not null
            || progress.OnlineTextPhase is not null || progress.EventFeedTail is not null
            || progress.AnnPhase is not { } next || chunk.Final is not null || chunk.EventId is not null
            || chunk.Message != AnnMaintenanceProgress.PhaseCompleted
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind)
            || !Next(next))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        phase = next;
    }

    private bool Next(AnnMaintenancePhase next) => (phase, next) switch
    {
        (null, AnnMaintenancePhase.Configure) => true,
        (null, AnnMaintenancePhase.Completed) => true,
        (AnnMaintenancePhase.Configure, AnnMaintenancePhase.Capture) => true,
        (AnnMaintenancePhase.Capture or AnnMaintenancePhase.Replay, AnnMaintenancePhase.Replay) => true,
        (AnnMaintenancePhase.Capture or AnnMaintenancePhase.Replay, AnnMaintenancePhase.Verify) => true,
        (AnnMaintenancePhase.Verify, AnnMaintenancePhase.NativeIndex) => true,
        (AnnMaintenancePhase.NativeIndex, AnnMaintenancePhase.Publish) => true,
        (AnnMaintenancePhase.Publish, AnnMaintenancePhase.Checkpoint) => true,
        (AnnMaintenancePhase.Checkpoint, AnnMaintenancePhase.Completed) => true,
        _ => false
    };
}
