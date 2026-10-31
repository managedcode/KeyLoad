using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class LiveQueryResultCompositionAssertions
{
    internal static async Task UnchangedAsync(TestDatabase db, string[] image, long position, string identity)
    {
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(Bytes(db)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(db.Store.Identity))).IsEqualTo(identity);
    }

    internal static async Task LiteralAsync(LiveQueryPage actual,
        System.Collections.Immutable.ImmutableArray<LiveQueryChange> changes, long through, bool more, long cut)
    {
        await Assert.That(string.IsNullOrWhiteSpace(actual.Cursor)).IsFalse();
        var expected = new LiveQueryPage(changes, actual.Cursor, through, more, cut);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static AstQueryRequest Query(TestDatabase db, bool compact)
        => new(db.Partition, new SelectQuery("orders", null,
            compact ? [new("/@id", "id")] : [new("*", "*")], null, [], 10), AllowFullScan: true);

    internal static string[] Bytes(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete fixture state exceeds its bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
