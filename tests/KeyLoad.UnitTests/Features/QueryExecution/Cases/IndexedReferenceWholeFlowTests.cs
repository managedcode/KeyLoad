using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class IndexedReferenceWholeFlowTests
{
    [Test]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    public async Task Kl013IndexedAndReferencePagesAgreeAndRejectedCursorsPreserveState(bool descending, int limit)
    {
        using var db = new TestDatabase();
        IndexedReferenceWholeFlowFixture.Seed(db);
        var engine = new QueryEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var indexed = IndexedReferenceWholeFlowFixture.Request(db, true, descending, limit);
        var reference = IndexedReferenceWholeFlowFixture.Request(db, false, descending, limit);
        var clock = new IndexedReferenceCursorClock(db.Database.EvaluationClock.GetUtcNow());
        var before = IndexedReferenceWholeFlowFixture.Bytes(db);
        var position = db.Store.Position;
        var indexedRows = await IndexedReferenceWholeFlowFixture.PagesAsync(db, engine, indexed, "index:status", clock);
        var scannedRows = await IndexedReferenceWholeFlowFixture.PagesAsync(db, engine, reference, "bounded-full-scan", clock);
        await IndexedReferenceWholeFlowFixture.LiteralRowsAsync(indexedRows, descending);
        await IndexedReferenceWholeFlowFixture.LiteralRowsAsync(scannedRows, descending);
        await Assert.That(IndexedReferenceWholeFlowFixture.Projection(indexedRows))
            .IsEquivalentTo(IndexedReferenceWholeFlowFixture.Projection(scannedRows), CollectionOrdering.Matching);
        var first = engine.Execute("root", indexed, clock);
        await Assert.That(first.Cursor).IsNotNull();
        var token = first.Cursor!;
        var corrupt = token[..(token.Length / 2)] + (token[token.Length / 2] == 'A' ? "B" : "A") + token[(token.Length / 2 + 1)..];
        QueryPage? partial = null;
        var tampered = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.Execute("root", indexed with { Cursor = corrupt }, clock));
        await Assert.That(tampered.Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(tampered.Message).IsEqualTo("The query cursor is invalid.");
        await Assert.That(partial).IsNull();
        var expiredClock = new IndexedReferenceCursorClock(clock.GetUtcNow().Add(UnitExecutionOptions.QueryExecution().Value.CursorLifetime).AddTicks(1));
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.Execute("root", indexed with { Cursor = token }, expiredClock));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.CursorExpired);
        await Assert.That(expired.Message).IsEqualTo("The query cursor no longer has a valid authorized read cut.");
        await Assert.That(partial).IsNull();
        var healthy = engine.Execute("root", indexed with { Cursor = token }, clock);
        await Assert.That(healthy.AccessPath).IsEqualTo("index:status");
        await Assert.That(healthy.CutPosition).IsEqualTo(position);
        await Assert.That(IndexedReferenceWholeFlowFixture.Projection(healthy.Rows))
            .IsEquivalentTo(IndexedReferenceWholeFlowFixture.Projection(indexedRows.Skip(limit).Take(limit)), CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(IndexedReferenceWholeFlowFixture.Bytes(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }
}
