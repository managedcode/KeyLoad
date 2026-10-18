using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Validates the exact completed online-text phases of one verified signed parent request.</summary>
internal sealed class OnlineTextMaintenanceStreamShape(Guid requestId)
{
    private OnlineTextPhase? phase;
    internal bool Completed => phase is null or OnlineTextPhase.RetirementMarked;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int count)
    {
        if (chunk.Kind != CqrsStreamChunkKind.Progress || chunk.Sequence != count
            || chunk.ProgressResult is not { IsSuccess: true, Value: not null } result
            || result.Problem is not null || result.Value is not { } progress
            || progress.RequestId != requestId || progress.AnnPhase is not null || progress.TextPhase is not null
            || progress.OnlineTextPhase is not { } next || chunk.Final is not null || chunk.EventId is not null
            || chunk.Message != OnlineTextProgress.PhaseCompleted
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind)
            || !Next(next))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        phase = next;
    }

    private bool Next(OnlineTextPhase next) => (phase, next) switch
    {
        (null, OnlineTextPhase.Captured) => true,
        (OnlineTextPhase.Captured, OnlineTextPhase.Seeded) => true,
        (OnlineTextPhase.Seeded, OnlineTextPhase.CaughtUp) => true,
        (OnlineTextPhase.CaughtUp, OnlineTextPhase.Validated) => true,
        (OnlineTextPhase.Validated, OnlineTextPhase.Published) => true,
        (OnlineTextPhase.Published, OnlineTextPhase.RetirementMarked) => true,
        _ => false
    };
}
