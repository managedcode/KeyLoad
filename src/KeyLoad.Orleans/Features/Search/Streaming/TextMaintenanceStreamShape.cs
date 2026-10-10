using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

/// <summary>Validates completed native text phases of one original signed maintenance operation.</summary>
internal sealed class TextMaintenanceStreamShape(Guid requestId)
{
    private TextIndexMaintenancePhase? phase;
    internal bool Completed => phase == TextIndexMaintenancePhase.Completed;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int count)
    {
        if (chunk.Sequence != count || chunk.ProgressResult is not { IsSuccess: true, Value: not null } result
            || result.Problem is not null || result.Value is not { } progress
            || progress.RequestId != requestId || progress.AnnPhase is not null || progress.MovePhase is not null
            || progress.OnlineTextPhase is not null || progress.TextPhase is not { } next
            || chunk.Final is not null || chunk.EventId is not null || chunk.Message != TextMaintenanceProgress.PhaseCompleted
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind)
            || !Next(next))
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        phase = next;
    }

    private bool Next(TextIndexMaintenancePhase next) => (phase, next) switch
    {
        (null, TextIndexMaintenancePhase.Configure) => true,
        (TextIndexMaintenancePhase.Configure, TextIndexMaintenancePhase.Capture) => true,
        (TextIndexMaintenancePhase.Capture or TextIndexMaintenancePhase.Checkpoint, TextIndexMaintenancePhase.NativeIndex) => true,
        (TextIndexMaintenancePhase.NativeIndex, TextIndexMaintenancePhase.Publish) => true,
        (TextIndexMaintenancePhase.Publish, TextIndexMaintenancePhase.Checkpoint) => true,
        (TextIndexMaintenancePhase.Capture or TextIndexMaintenancePhase.Checkpoint, TextIndexMaintenancePhase.Completed) => true,
        _ => false
    };
}
