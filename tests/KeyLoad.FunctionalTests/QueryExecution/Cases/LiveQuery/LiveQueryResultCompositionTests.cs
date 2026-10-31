using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class LiveQueryResultCompositionTests
{
    [Test]
    public async Task HiddenOutboxPagesResumeFromTheOriginalCursorWithLiteralPages()
    {
        using var db = new TestDatabase(new DatabaseLimits());
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("hidden", ResourceKind.Collection);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var query = LiveQueryResultCompositionAssertions.Query(db, true);
        var start = engine.StartLiveQuery("root", new(query));
        await Assert.That(string.IsNullOrWhiteSpace(start.Cursor)).IsFalse();
        var expectedStart = new LiveQuerySnapshot([], start.Cursor, 0L, db.Store.Position);
        await Assert.That(JsonDefaults.Serialize(start).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedStart))).IsTrue();
        var hiddenBody = "{\"payload\":\"" + new string('h', 36_000) + "\"}";
        db.Commit(new PutDocument("hidden", "first", hiddenBody));
        db.Commit(new PutDocument("hidden", "second", hiddenBody));
        var receipt = db.Commit(new PutDocument("orders", "a", "{}"));
        var image = LiveQueryResultCompositionAssertions.Bytes(db);
        var position = db.Store.Position;
        var identity = Convert.ToHexString(JsonDefaults.Serialize(db.Store.Identity));
        var request = new ReadLiveQueryRequest(query, start.Cursor, Limit: 10);
        var first = engine.ReadLiveQuery("root", request with
        {
            Limit = 1
        });
        await LiveQueryResultCompositionAssertions.LiteralAsync(first, [], start.ThroughSequence + 1, true, position);
        await LiveQueryResultCompositionAssertions.UnchangedAsync(db, image, position, identity);
        var second = engine.ReadLiveQuery("root", request with
        {
            Cursor = first.Cursor,
            Limit = 1
        });
        await LiveQueryResultCompositionAssertions.LiteralAsync(second, [], start.ThroughSequence + 2, true, position);
        await LiveQueryResultCompositionAssertions.UnchangedAsync(db, image, position, identity);
        var change = new LiveQueryChange(start.ThroughSequence + 3, receipt.Token,
            LiveQueryChangeKind.Upsert, db.ExpectedReference(new(db.Partition, "orders", "a")), 1L,
            new QueryRow("a", 1L, "{\"id\":\"a\"}", RedactedFields: []));
        var healthy = engine.ReadLiveQuery("root", request with
        {
            Cursor = second.Cursor,
            Limit = 1
        });
        await LiveQueryResultCompositionAssertions.LiteralAsync(healthy, [change], start.ThroughSequence + 3, false, position);
        await LiveQueryResultCompositionAssertions.UnchangedAsync(db, image, position, identity);
        var replay = engine.ReadLiveQuery("root", request with
        {
            Cursor = second.Cursor,
            Limit = 1
        });
        await LiveQueryResultCompositionAssertions.LiteralAsync(replay, [change], start.ThroughSequence + 3, false, position);
        await LiveQueryResultCompositionAssertions.UnchangedAsync(db, image, position, identity);
        var terminal = engine.ReadLiveQuery("root", request with
        {
            Cursor = healthy.Cursor,
            Limit = 1
        });
        await LiveQueryResultCompositionAssertions.LiteralAsync(terminal, [], start.ThroughSequence + 3, false, position);
        await LiveQueryResultCompositionAssertions.UnchangedAsync(db, image, position, identity);
    }
}
