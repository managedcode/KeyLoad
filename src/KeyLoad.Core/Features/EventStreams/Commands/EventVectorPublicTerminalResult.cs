using System.Collections.Immutable;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventFeedPublicResultVersion = 1;
    private const string InvalidEventFeedPublicTerminal = "The original complete event feed terminal result requires recovery.";

    private EventVectorControlResult RetainEventVectorPublicResult(EventVectorControlPhase phase,
        EventVectorControlResult result, EventVectorMap? originalMap, EventVectorAdmissionPolicy admission)
    {
        if (!EventVectorPublicTerminalResult.IsTerminal(phase.Action))
        { return result; }
        var map = result.Map ?? originalMap
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventFeedPublicTerminal);
        if (map.MapId != phase.OriginalRequest.MapId || map.ControlPartition != phase.OriginalRequest.ControlPartition
            || result.Receipt.CommandId != result.CommandId
            || result.Offer is { } offer && (offer.MapId != map.MapId || offer.MapRevision != map.Revision))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventFeedPublicTerminal); }
        var pages = phase.Action == EventVectorControlAction.Offer
            ? NativeSerialization.Deserialize<ImmutableArray<EventSourcePage>>(result.OriginalSourcePageBytes.Span)
            : ImmutableArray<EventSourcePage>.Empty;
        if (pages.IsDefault)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventFeedPublicTerminal); }
        var publicResult = new EventFeedControlResult
        {
            Version = EventFeedPublicResultVersion,
            PublicCommandId = phase.OriginalRequest.CommandId,
            MapId = map.MapId,
            Revision = map.Revision,
            CoverageGeneration = map.CoverageGeneration,
            Released = map.Released,
            Retained = !map.Released,
            Cursor = map.Released ? null : SignEventVectorToken(map, EventVectorMapTokenPurpose, map.SourceManifestDigest),
            OfferDigest = result.Offer is null ? null
                : SignEventVectorToken(map, EventVectorOfferTokenPurpose, result.Offer.PageDigest),
            Pages = pages,
            Receipt = result.Receipt
        };
        admission.RequireEncodedBytes(NativeSerialization.Measure(publicResult));
        var original = NativeSerialization.Serialize(publicResult);
        admission.RequireEncodedBytes(original.Length);
        return result with { OriginalPublicResultBytes = original };
    }
}

internal static class EventVectorPublicTerminalResult
{
    internal static bool IsTerminal(EventVectorControlAction action) => action is
        EventVectorControlAction.PublishOpen or EventVectorControlAction.Offer
        or EventVectorControlAction.PublishAcknowledgement or EventVectorControlAction.PublishRefresh
        or EventVectorControlAction.CompleteRelease;
}
