using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal sealed class EventFeedTailStreamShape(Guid requestId, EventFeedControlRequest original)
{
    private const int Version = 1;
    private const int NoDeliveries = 0;
    private const long NoPosition = 0;
    private int deliveries;
    private long lastPosition;
    internal bool HasDelivery => deliveries > NoDeliveries;

    internal void Admit(CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> chunk, int count)
    {
        if (chunk.Sequence != count || chunk.ProgressResult is not { IsSuccess: true, Value: not null } result
            || result.Problem is not null || result.Value is not { } progress || progress.RequestId != requestId
            || progress.AnnPhase is not null || progress.TextPhase is not null || progress.MovePhase is not null
            || progress.OnlineTextPhase is not null || progress.EventFeedTail is not { Version: Version } next
            || chunk.Final is not null || chunk.EventId is not null || chunk.Message is not null
            || chunk.EventType != CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>.ResolveEventType(chunk.Kind))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var value = next.OriginalOfferResult;
        if (value is null || value.Version != Version || value.MapId != original.MapId
            || value.PublicCommandId == Guid.Empty || value.CoverageGeneration != original.ExpectedCoverageGeneration
            || value.Revision < original.ExpectedRevision || value.Released || !value.Retained
            || string.IsNullOrEmpty(value.OfferDigest) || value.Pages.IsDefault || value.Receipt is not { } receipt
            || receipt.CommandId == Guid.Empty || receipt.Token is null || receipt.Token.Position <= NoPosition
            || receipt.Token.Position <= lastPosition || deliveries >= original.Limit)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        deliveries++;
        lastPosition = receipt.Token.Position;
    }
}
