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

    internal const int Cap = 4096;
    internal static AstQueryRequest Query(TestDatabase db, bool compact)
        => new(db.Partition, new SelectQuery("orders", null,
            compact ? [new("/@id", "id")] : [new("*", "*")], null, [], 10), AllowFullScan: true);

    internal static string Body(string id) => "{\"id\":\"" + id + "\",\"payload\":\"" + new string('x', 2200) + "\"}";

    internal static string[] Bytes(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete fixture state exceeds its bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal static async Task PageAsync(TestDatabase db, LiveQueryPage page, long through, bool more,
        long sequence, string[] ids, CommitToken[] commits, bool compact)
    {
        await Assert.That(page.ThroughSequence).IsEqualTo(through);
        await Assert.That(page.HasMore).IsEqualTo(more);
        await Assert.That(page.CutPosition).IsEqualTo(db.Store.Position);
        await Assert.That(page.Cursor).IsNotNull();
        await Assert.That(page.Changes.Select(change => change.Reference.Id)).IsEquivalentTo(ids, CollectionOrdering.Matching);
        for (var index = 0; index < ids.Length; index++)
        {
            var change = page.Changes[index];
            await Assert.That(change.Sequence).IsEqualTo(sequence + index);
            await Assert.That(change.Commit).IsEqualTo(commits[index]);
            await Assert.That(change.Reference).IsEqualTo(db.ExpectedReference(new(db.Partition, "orders", ids[index])));
            await Assert.That(change.Kind).IsEqualTo(LiveQueryChangeKind.Upsert);
            await Assert.That(change.Revision).IsEqualTo(2L);
            await Assert.That(change.Row!.EntityId).IsEqualTo(ids[index]);
            await Assert.That(change.Row.Revision).IsEqualTo(2L);
            await Assert.That(change.Row.Json).IsEqualTo(compact ? "{\"id\":\"" + ids[index] + "\"}" : Body(ids[index]));
            await Assert.That(change.Row.Redacted).IsFalse();
            await Assert.That(change.Row.RedactedFields ?? []).IsEmpty();
            await Assert.That(change.Row.Sources).IsNull();
        }
    }
}
