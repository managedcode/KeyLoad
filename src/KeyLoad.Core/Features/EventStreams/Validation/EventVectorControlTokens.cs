using System.Security.Cryptography;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorTokenVersion = 1;
    private const string EventVectorMapTokenPurpose = "event-vector-map-v1";
    private const string EventVectorOfferTokenPurpose = "event-vector-offer-v1";
    private const string InvalidEventVectorToken = "The original event vector token is invalid.";

    private void RequireEventVectorControlTokens(EventVectorMap map, EventVectorOwnedInventory inventory,
        EventVectorControlPhase phase)
    {
        var request = phase.OriginalRequest;
        var requiresCursor = request.Action is EventFeedControlAction.Offer or EventFeedControlAction.Tail
            or EventFeedControlAction.Acknowledge or EventFeedControlAction.Refresh;
        if (requiresCursor || request.Cursor is not null)
        {
            if (request.Cursor is null)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorToken); }
            var cursor = Verify<EventVectorToken>(request.Cursor);
            RequireEventVectorToken(map, cursor, EventVectorMapTokenPurpose, map.SourceManifestDigest);
        }
        if (request.Action != EventFeedControlAction.Acknowledge)
        { return; }
        if (request.OfferDigest is null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorToken); }
        var offer = ReadEventVectorOffer(map, inventory);
        var token = Verify<EventVectorToken>(request.OfferDigest);
        RequireEventVectorToken(map, token, EventVectorOfferTokenPurpose, offer.PageDigest);
        if (offer.MapRevision != map.Revision || offer.ExpiresAt != map.ExpiresAt
            || !offer.Entries.Span.SequenceEqual(phase.OriginalEncodedEntries.Span)
                && phase.Action == EventVectorControlAction.PublishAcknowledgement)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorToken); }
    }

    private void RequireEventVectorToken(EventVectorMap map, EventVectorToken token,
        string purpose, ReadOnlyMemory<byte> digest)
    {
        if (token is null || token.Version != EventVectorTokenVersion || token.Purpose != purpose
            || token.MapId != map.MapId || token.ControlPartition != map.ControlPartition
            || token.MapRevision != map.Revision || token.CoverageGeneration != map.CoverageGeneration
            || token.PrincipalId != map.PrincipalId || token.ControlPolicyEpoch != map.ControlPolicyEpoch
            || token.ControlIncarnation != map.ControlIncarnation || token.ExpiresAt != map.ExpiresAt
            || !CryptographicOperations.FixedTimeEquals(token.ManifestDigest.Span, digest.Span))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidEventVectorToken); }
        if (token.ExpiresAt <= EvaluationClock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.CursorExpired, InvalidEventVectorToken); }
    }

    private static EventVectorOffer ReadEventVectorOffer(EventVectorMap map, EventVectorOwnedInventory inventory)
    {
        var key = EventVectorKeys.Offer(map.ControlPartition, map.MapId);
        foreach (var row in inventory.PayloadRows)
        {
            if (row.Key.Span.SequenceEqual(key))
            { return NativeSerialization.Deserialize<EventVectorOffer>(row.Value.Span); }
        }
        throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidEventVectorToken);
    }

    private string SignEventVectorToken(EventVectorMap map, string purpose, ReadOnlyMemory<byte> digest)
        => Sign(new EventVectorToken
        {
            Version = EventVectorTokenVersion,
            Purpose = purpose,
            MapId = map.MapId,
            ControlPartition = map.ControlPartition,
            MapRevision = map.Revision,
            CoverageGeneration = map.CoverageGeneration,
            ManifestDigest = digest,
            PrincipalId = map.PrincipalId,
            ControlPolicyEpoch = map.ControlPolicyEpoch,
            ControlIncarnation = map.ControlIncarnation,
            ExpiresAt = map.ExpiresAt
        });
}
