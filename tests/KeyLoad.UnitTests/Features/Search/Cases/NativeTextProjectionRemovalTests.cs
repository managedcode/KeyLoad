using KeyLoad.Query;
using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionRemovalTests
{
    private const string Principal = "root";
    private const string Collection = "native-removal";
    private const string ProjectionDirectory = "disposable-text";
    private const string Field = "/text";
    private const string OldTerm = "needle";
    private const string NewTerm = "fresh";
    private const string One = "one";
    private const string Two = "two";
    private const string Three = "three";
    private const string OldJson = "{\"text\":\"needle\"}";
    private const string NewJson = "{\"text\":\"fresh\"}";
    private const long FirstRevision = 1;
    private const long NextRevision = 2;
    private const int FusionConstant = 60;
    private const int FirstRank = 1;
    private const int SecondRank = 2;
    private const int SnapshotBound = 128;

    [Test]
    public async Task RemovedNativeProjectionRebuildsOnlyCurrentLiteralRowsAndHealthyWritesRemainVisible()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, One, OldJson), new PutDocument(Collection, Two, OldJson));
        var root = Path.Combine(db.Directory, ProjectionDirectory);
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = new SearchRequest(db.Partition, Collection, Field, OldTerm);
        using (var initial = Create(db, root))
        {
            var rows = await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution(), initial)
                .SearchAsync(Principal, request, token);
            await LiteralAsync(db, rows, [One, Two], OldJson, FirstRevision, FirstRank);
        }
        Directory.Delete(root, recursive: true);
        await Assert.That(Directory.Exists(root)).IsFalse();
        db.Commit(new PutDocument(Collection, One, NewJson, ExpectedRevision: FirstRevision),
            new DeleteDocument(Collection, Two, FirstRevision));
        using var rebuilt = Create(db, root);
        var engine = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution(), rebuilt);
        var before = Snapshot(db);
        var position = db.Store.Position;
        await Assert.That(await engine.SearchAsync(Principal, request, token)).IsEmpty();
        await LiteralAsync(db, await engine.SearchAsync(Principal,
            request with { Text = NewTerm }, token), [One], NewJson, NextRevision, FirstRank);
        await Assert.That(Snapshot(db)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        db.Commit(new PutDocument(Collection, Three, NewJson));
        var healthy = await engine.SearchAsync(Principal, request with { Text = NewTerm }, token);
        await Assert.That(healthy.Select(row => row.Document.Reference.Id).ToArray())
            .IsEquivalentTo([One, Three], CollectionOrdering.Matching);
        await LiteralAsync(db, [healthy[0]], [One], NewJson, NextRevision, FirstRank);
        await LiteralAsync(db, [healthy[1]], [Three], NewJson, FirstRevision, SecondRank);
    }

    private static NativeTextProjection Create(TestDatabase db, string root)
        => new(root, UnitExecutionOptions.DatabaseLimits(db.Database.Limits), db.Store.Identity.NodeId,
            UnitNativeTextOptions.Execution());

    private static (string Key, string Value)[] Snapshot(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], SnapshotBound);
        if (page.HasMore)
        { throw new InvalidOperationException("The native removal fixture exceeds its complete snapshot bound."); }
        return page.Records.Select(row => (Convert.ToHexString(row.Key.Span), Convert.ToHexString(row.Value.Span))).ToArray();
    });

    private static async Task LiteralAsync(TestDatabase db, RankedDocument[] rows,
        string[] ids, string json, long revision, int firstRank)
    {
        await Assert.That(rows.Length).IsEqualTo(ids.Length);
        for (var index = 0; index < ids.Length; index++)
        {
            await Assert.That(rows[index].Document.Reference).IsEqualTo(new EntityRef(db.Partition, Collection, ids[index]));
            await Assert.That(rows[index].Document.Json).IsEqualTo(json);
            await Assert.That(rows[index].Document.Revision).IsEqualTo(revision);
            await Assert.That(rows[index].Document.Redacted).IsFalse();
            await Assert.That(rows[index].Document.RedactedFields).IsEmpty();
            await Assert.That(rows[index].Score).IsEqualTo(1d / (FusionConstant + firstRank + index));
        }
    }
}
