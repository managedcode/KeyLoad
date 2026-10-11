using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorOfferPageProjection
{
    private const int FirstOrdinal = 0;
    private const int BeforeFinalEvent = 1;
    private const long InitialCut = 0;
    private const string InvalidPage = "The original complete event vector source page is inconsistent.";

    internal static ImmutableArray<EventVectorEntry> Create(ImmutableArray<EventVectorEntry> acknowledged,
        ImmutableArray<EventSourcePage> actualPages, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        if (acknowledged.IsDefault || actualPages.IsDefault || acknowledged.Length != actualPages.Length)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPage); }
        admission.RequireEntryCount(acknowledged.Length);
        var result = ImmutableArray.CreateBuilder<EventVectorEntry>(acknowledged.Length);
        for (var ordinal = FirstOrdinal; ordinal < acknowledged.Length; ordinal++)
        { result.Add(Create(acknowledged[ordinal], actualPages[ordinal])); }
        return result.MoveToImmutable();
    }

    private static EventVectorEntry Create(EventVectorEntry original, EventSourcePage page)
    {
        if (original is null || page is null || page.Head is null || page.Source != original.Source
            || page.Events.IsDefault || string.IsNullOrEmpty(page.Cursor) || page.CutPosition < InitialCut || page.CutPosition < original.CapturedCut
            || page.Head.Generation != original.Source.Generation
            || page.Head.TailPosition < original.Position)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPage); }
        if (page.Head.FirstAvailablePosition > checked(original.Position + BeforeFinalEvent))
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, InvalidPage); }
        var position = original.Position;
        foreach (var value in page.Events)
        {
            if (value is null || value.Data is null || value.Source != original.Source || value.Position <= position
                || value.Position > page.Head.TailPosition || value.EventSequence < original.EventSequence)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidPage); }
            position = value.Position;
        }
        var final = page.Events.IsEmpty ? null : page.Events[page.Events.Length - BeforeFinalEvent];
        return original with
        {
            Position = final?.Position ?? original.Position,
            EventSequence = final?.EventSequence ?? original.EventSequence,
            CapturedCut = page.CutPosition,
            FirstAvailablePosition = page.Head.FirstAvailablePosition,
            CapturedTailPosition = page.Head.TailPosition
        };
    }
}
