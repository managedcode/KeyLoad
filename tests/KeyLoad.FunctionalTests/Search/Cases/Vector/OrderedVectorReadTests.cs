using System.Globalization;
using KeyLoad.Query;
using TUnit.Assertions.Enums;
using static KeyLoad.FunctionalTests.Search.FilteredSearchTestSupport;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class OrderedVectorReadTests
{
    private const string Root = "root";
    private const string Reader = "ordered-reader";
    private const string Owner = "alice";
    private const string Hidden = "hidden";
    private const string Stale = "stale";
    private const string First = "a";
    private const string PrivateUse = "\uE000";
    private const string Supplementary = "\U00010000";
    private const string Json = "{}";
    private const int GapRows = 768;
    private const int BatchRows = 64;
    private const int ResultLimit = 3;

    [Test]
    public async Task SparseExactSearchKeepsOrdinalTiesAndCurrentRowVisibility()
    {
        using var database = Create();
        AddGap(database);
        database.Commit(new PutDocument(Collection, First, Json, Access: new(Owner)),
            new PutDocument(Collection, Hidden, Json, Access: new("bob")),
            new PutDocument(Collection, Stale, Json, Access: new(Owner)),
            new PutDocument(Collection, PrivateUse, Json, Access: new(Owner)),
            new PutDocument(Collection, Supplementary, Json, Access: new(Owner)),
            new PutVector(Collection, First, VectorField, [0, 1], Space, Revision),
            new PutVector(Collection, Hidden, VectorField, [1, 0], Space, Revision),
            new PutVector(Collection, Stale, VectorField, [1, 0], Space, Revision),
            new PutVector(Collection, PrivateUse, VectorField, [1, 0], Space, Revision),
            new PutVector(Collection, Supplementary, VectorField, [1, 0], Space, Revision));
        database.Commit(new PutDocument(Collection, Stale, """{"current":true}""", Revision, new(Owner), ExplicitReplacement: true));
        PersistReader(database, Reader, Capability.VectorSearch, [VectorUseGrant], Owner, restrictRows: true);
        var cut = database.Store.Position;
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = VectorRequest(database);
        var results = await engine.SearchAsync(Reader, request, TestContext.Current!.Execution.CancellationToken);
        // Native UTF-8 order places the private-use ID first. Exact score ties use ordinal UTF-16.
        var expected = new[] { Supplementary, PrivateUse, First }
            .Select(id => database.ExpectedReference(new(database.Partition, Collection, id)));
        await Assert.That(results.Select(hit => hit.Document.Reference)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(results.Select(hit => hit.Score))
            .IsEquivalentTo(new[] { 1d / 61, 1d / 62, 1d / 63 }, CollectionOrdering.Matching);
        await Assert.That(results.All(hit => hit.Document.Revision == Revision && hit.Document.Json == Json)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FrameIdentityMismatchFailsBeforeReturningAnyOtherRow(bool changeId)
    {
        using var database = Create();
        database.Commit(new PutDocument(Collection, First, Json), new PutDocument(Collection, Hidden, Json),
            new PutVector(Collection, First, VectorField, [1, 0], Space, Revision));
        var token = TestContext.Current!.Execution.CancellationToken;
        var key = KeySpace.Partition("vector", database.Partition, Collection, VectorField, First);
        var original = database.Store.Read(view => view.ReadOwnedValue(key))!;
        var vector = OrleansSerialization.Deserialize<VectorStorageRecord>(original);
        var changed = changeId ? vector with { DocumentId = Hidden } : vector with { Field = "/different" };
        StoreVector(database, key, OrleansSerialization.Serialize(changed));
        var cut = database.Store.Position;
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        try
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.SearchAsync(Root, VectorRequest(database), token));
            await Assert.That(error!.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo("The stored vector identity does not match its canonical key.");
            await Assert.That(database.Store.Position).IsEqualTo(cut);
        }
        finally
        {
            StoreVector(database, key, original);
        }
        var healthy = await engine.SearchAsync(Root, VectorRequest(database), token);
        await Assert.That(healthy.Single().Document.Reference.Id).IsEqualTo(First);
    }

    private static SearchRequest VectorRequest(TestDatabase database)
        => new(database.Partition, Collection, VectorField: VectorField, Vector: [1, 0], Space: Space, Limit: ResultLimit);

    private static void AddGap(TestDatabase database)
    {
        foreach (var batch in Enumerable.Range(0, GapRows).Chunk(BatchRows))
        {
            database.Commit([.. batch.Select(index => (Mutation)new PutDocument(Collection,
                "gap-" + index.ToString("D4", CultureInfo.InvariantCulture), Json))]);
        }
    }

    private static void StoreVector(TestDatabase database, byte[] key, byte[] frame)
        => database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, frame);
            return true;
        });
}
