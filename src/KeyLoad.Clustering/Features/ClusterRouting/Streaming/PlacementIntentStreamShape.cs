using KeyLoad.Core.Features.ClusterRouting.Contracts;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Admits only ordered original retired-move observations for one signed placement intent.</summary>
internal sealed class PlacementIntentStreamShape(Guid requestId, Guid intentId, long expectedRevision)
{
    private readonly HashSet<Guid> observedMoves = [];
    private PlacementIntentProgressV1? last;

    // Completed intents and zero-move plans return their retained result without progress.
    internal bool Completed => last is null || last.CompletedMoves == last.TotalMoves;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int count)
    {
        if (chunk.Kind != CqrsStreamChunkKind.Progress || chunk.Sequence != count
            || chunk.ProgressResult is not { IsSuccess: true, Value: not null } result
            || result.Problem is not null || result.Value is not { } progress
            || progress.RequestId != requestId || progress.TextPhase is not null || progress.AnnPhase is not null
            || progress.MovePhase is not null || progress.OnlineTextPhase is not null || progress.EventFeedTail is not null
            || progress.PlacementIntent is not { } next || chunk.Final is not null || chunk.EventId is not null
            || chunk.Message != PlacementIntentNativeProtocol.Progress
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind)
            || !Next(next) || !observedMoves.Add(next.OriginalMoveId))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        last = next;
    }

    private bool Next(PlacementIntentProgressV1 next)
        => intentId != Guid.Empty && expectedRevision >= TenantPlacementPolicyProtocol.EmptyRevision
            && next.IntentId == intentId && next.Revision > expectedRevision
            && next.CompletedMoves >= TenantPlacementPolicyProtocol.MinimumMoves
            && next.CompletedMoves <= next.TotalMoves
            && next.TotalMoves is >= TenantPlacementPolicyProtocol.MinimumMoves and <= TenantPlacementPolicyProtocol.MaximumMoves
            && next.OriginalMoveId != Guid.Empty && next.ActualPhase == PartitionMovePhase.Retired
            && (last is null || next.TotalMoves == last.TotalMoves
                && next.CompletedMoves == last.CompletedMoves + TenantPlacementPolicyProtocol.MinimumMoves
                && next.Revision > last.Revision);
}
