using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class LiveQueryResultCompositionTests
{
    [Test]
    public async Task LiveDeltaCumulativeCeilingRejectsBeforeRetentionAndOriginalPageBoundaryStillResumes()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "a", "{}"), new PutDocument("orders", "b", "{}"));
        var original = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var bounded = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution(new QueryExecutionOptions
        { MaximumResultBytes = LiveQueryResultCompositionAssertions.Cap }));
        var full = LiveQueryResultCompositionAssertions.Query(db, false);
        var compact = LiveQueryResultCompositionAssertions.Query(db, true);
        var start = bounded.StartLiveQuery("root", new(full));
        var smallStart = bounded.StartLiveQuery("root", new(compact));
        await Assert.That(start.Rows.Select(row => row.EntityId)).IsEquivalentTo(new[] { "a", "b" }, CollectionOrdering.Matching);
        await Assert.That(start.Rows.Select(row => row.Json)).IsEquivalentTo(new[] { "{}", "{}" }, CollectionOrdering.Matching);
        await Assert.That(start.ThroughSequence).IsEqualTo(smallStart.ThroughSequence);
        var firstCommit = db.Commit(new PutDocument("orders", "a", LiveQueryResultCompositionAssertions.Body("a"), 1));
        var secondCommit = db.Commit(new PutDocument("orders", "b", LiveQueryResultCompositionAssertions.Body("b"), 1));
        var before = LiveQueryResultCompositionAssertions.Bytes(db);
        var position = db.Store.Position;
        var request = new ReadLiveQueryRequest(full, start.Cursor, Limit: 10, MaxBytes: db.Database.Limits.MaxBatchBytes);
        var baseline = original.ReadLiveQuery("root", request);
        var sizes = baseline.Changes.Select(change => JsonDefaults.Serialize(change).Length).ToArray();
        await Assert.That(sizes.Length).IsEqualTo(2);
        await Assert.That(sizes.All(size => size < LiveQueryResultCompositionAssertions.Cap)).IsTrue();
        await Assert.That(sizes.Sum()).IsGreaterThan(LiveQueryResultCompositionAssertions.Cap);
        await Assert.That(JsonDefaults.Serialize(original.ReadLiveQuery("root", request with { Limit = 1 })).Length)
            .IsLessThan(LiveQueryResultCompositionAssertions.Cap);
        LiveQueryPage? partial = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = bounded.ReadLiveQuery("root", request));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo("The query result byte budget is exceeded.");
        await Assert.That(partial).IsNull();
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(LiveQueryResultCompositionAssertions.Bytes(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        var healthy = bounded.ReadLiveQuery("root", new(compact, smallStart.Cursor, Limit: 10,
            MaxBytes: db.Database.Limits.MaxBatchBytes));
        await LiveQueryResultCompositionAssertions.PageAsync(db, healthy, start.ThroughSequence + 2, false,
            start.ThroughSequence + 1, ["a", "b"], [firstCommit.Token, secondCommit.Token], true);
        await OriginalPageBoundaryAsync(db, bounded, request, start.ThroughSequence, sizes.Max(), firstCommit.Token, secondCommit.Token);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(LiveQueryResultCompositionAssertions.Bytes(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    private static async Task OriginalPageBoundaryAsync(TestDatabase db, QueryEngine engine,
        ReadLiveQueryRequest request, long tail, int pageBytes, CommitToken firstCommit, CommitToken secondCommit)
    {
        var first = engine.ReadLiveQuery("root", request with { MaxBytes = pageBytes });
        await LiveQueryResultCompositionAssertions.PageAsync(db, first, tail + 1, true, tail + 1, ["a"], [firstCommit], false);
        var second = engine.ReadLiveQuery("root", request with { Cursor = first.Cursor, MaxBytes = pageBytes });
        await LiveQueryResultCompositionAssertions.PageAsync(db, second, tail + 2, false, tail + 2, ["b"], [secondCommit], false);
        var terminal = engine.ReadLiveQuery("root", request with { Cursor = second.Cursor, MaxBytes = pageBytes });
        await Assert.That(terminal.Changes).IsEmpty();
        await Assert.That(terminal.ThroughSequence).IsEqualTo(tail + 2);
        await Assert.That(terminal.HasMore).IsFalse();
        await Assert.That(terminal.CutPosition).IsEqualTo(db.Store.Position);
    }
}
