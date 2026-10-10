using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorControlPages
{
    internal const int FirstOrdinal = 0;
    private const string InvalidPages = "The complete acknowledged event vector pages are inconsistent.";

    internal static ImmutableArray<EventVectorEntry> Entries(EventVectorOwnedInventory inventory,
        EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidPages);
        var prefix = EventVectorKeys.Pages(map.ControlPartition, map.MapId);
        var pages = new SortedDictionary<int, EventVectorPage>();
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.StartsWith(prefix))
            { continue; }
            var page = NativeSerialization.Deserialize<EventVectorPage>(row.Value.Span);
            if (!pages.TryAdd(page.PageOrdinal, page))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidPages); }
        }
        var entries = ImmutableArray.CreateBuilder<EventVectorEntry>();
        for (var ordinal = FirstOrdinal; ordinal < map.PageCount; ordinal++)
        {
            if (!pages.TryGetValue(ordinal, out var page))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidPages); }
            var values = EventVectorEntryEncoding.Decode(page.Entries, page.Checksum, admission);
            admission.RequireEntryCount(checked(entries.Count + values.Length));
            entries.AddRange(values);
        }
        return entries.ToImmutable();
    }
}
