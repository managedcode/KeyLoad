using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorOriginalPublicResult
{
    private const int Version = 1;
    private const int FirstElement = 0;
    private const string Invalid = "The original complete public event vector result requires recovery.";

    internal static EventFeedControlResult? Read(EventVectorControlResult result,
        EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(result.OriginalPublicResultBytes.Length);
        if (!EventVectorPublicTerminalResult.IsTerminal(phase.Action))
        {
            if (!result.OriginalPublicResultBytes.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
            }
            return null;
        }
        if (result.OriginalPublicResultBytes.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var value = NativeSerialization.Deserialize<EventFeedControlResult>(result.OriginalPublicResultBytes.Span);
        if (value is null || value.Version != Version || value.PublicCommandId != phase.OriginalRequest.CommandId
            || value.MapId != phase.OriginalRequest.MapId || value.Pages.IsDefault || value.Receipt is null
            || value.Receipt.CommandId != result.CommandId || value.Receipt.Token != result.Receipt.Token
            || value.Receipt.Durability != result.Receipt.Durability || value.Receipt.Mutations.IsDefault
            || !value.Receipt.Mutations.SequenceEqual(result.Receipt.Mutations)
            || value.Released == value.Retained
            || value.Released != (value.Cursor is null) || (value.OfferDigest is null) != (result.Offer is null)
            || result.Map is { } map && (value.Revision != map.Revision
                || value.CoverageGeneration != map.CoverageGeneration || value.Released != map.Released)
            || result.Offer is { } offer && (value.Revision != offer.MapRevision
                || value.CoverageGeneration != phase.ExpectedCoverageGeneration || value.Released))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var pages = phase.Action == EventVectorControlAction.Offer
            ? NativeSerialization.Deserialize<ImmutableArray<EventSourcePage>>(result.OriginalSourcePageBytes.Span)
            : ImmutableArray<EventSourcePage>.Empty;
        RequirePages(value.Pages, pages);
        return value;
    }

    private static void RequirePages(ImmutableArray<EventSourcePage> actual, ImmutableArray<EventSourcePage> original)
    {
        if (original.IsDefault || actual.Length != original.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        for (var index = FirstElement; index < original.Length; index++)
        {
            var left = actual[index];
            var right = original[index];
            if (left is null || right is null || left.Source != right.Source || left.Head != right.Head
                || left.Cursor != right.Cursor || left.CutPosition != right.CutPosition || left.HasMore != right.HasMore
                || left.Events.IsDefault || right.Events.IsDefault || !left.Events.SequenceEqual(right.Events))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
            }
        }
    }
}
