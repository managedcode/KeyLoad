using KeyLoad.Core;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class SearchStreamingTests
{
    private const string Collection = "orders";
    private const string TextPath = "/text";
    private const string VectorPath = "/embedding";
    private const double Tolerance = 0.0000000000005;

    [Test]
    public async Task FullBranchRanksSelectSharedSecondPlaceAtLimitOne()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "text-first", "{\"text\":\"alpha alpha alpha\"}"),
            new PutDocument(Collection, "shared", "{\"text\":\"alpha\"}"),
            new PutDocument(Collection, "vector-first", "{}"));
        var space = new VectorSpace("search", 2, DistanceMetric.DotProduct, "test", "1");
        db.Commit(new PutVector(Collection, "shared", VectorPath, [0.5f, 0], space, 1),
            new PutVector(Collection, "vector-first", VectorPath, [1, 0], space, 1));

        var result = await Assert.That(await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root",
            new(db.Partition, Collection, TextPath, "alpha", VectorPath, [1, 0], space, Limit: 1),
            TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
        await Assert.That(result.Document.Reference.Id).IsEqualTo("shared");
        await Assert.That(result.Score).IsEqualTo(2.0 / 62).Within(Tolerance);
    }

    [Test]
    public async Task CorpusStatisticsIncludeMissingAndLongNonmatchingRows()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "z", "{\"text\":\"beta beta gamma gamma gamma gamma gamma gamma gamma gamma\"}"),
            new PutDocument(Collection, "a", "{\"text\":\"alpha\"}"),
            new PutDocument(Collection, "missing", "{}"),
            new PutDocument(Collection, "other", "{\"text\":\"" + string.Join(' ', Enumerable.Repeat("gamma", 100)) + "\"}"));

        var result = await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root", new(db.Partition, Collection, TextPath, "alpha beta"),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Select(row => row.Document.Reference.Id))
            .IsEquivalentTo(new[] { "z", "a" }, CollectionOrdering.Matching);
        await Assert.That(result[0].Score).IsEqualTo(1.0 / 61).Within(Tolerance);
        await Assert.That(result[1].Score).IsEqualTo(1.0 / 62).Within(Tolerance);
    }

    [Test]
    public async Task EqualBranchScoresUseOrdinalDocumentIds()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "z", "{\"text\":\"alpha\"}"),
            new PutDocument(Collection, "a", "{\"text\":\"alpha\"}"));

        var result = await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root", new(db.Partition, Collection, TextPath, "alpha"),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Select(row => row.Document.Reference.Id))
            .IsEquivalentTo(new[] { "a", "z" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task VectorCandidatesRespectPersistedRowPolicyAndTypedSpace()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "alice", "{}", Access: new("alice")),
            new PutDocument(Collection, "bob", "{}", Access: new("bob")));
        var space = new VectorSpace("search", 2, DistanceMetric.DotProduct, "test", "1");
        db.Commit(new PutVector(Collection, "alice", VectorPath, [1, 0], space, 1),
            new PutVector(Collection, "bob", VectorPath, [1, 0], space, 1));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("alice", "tenant",
            [new("database", Collection, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], [])
        { OwnerId = "alice", RestrictRows = true })).Get<PrincipalRecord>();
        var search = new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution());
        var request = new SearchRequest(db.Partition, Collection, VectorField: VectorPath, Vector: [1, 0], Space: space);

        var visible = await Assert.That(await search.SearchAsync("alice", request,
            TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
        await Assert.That(visible.Document.Reference.Id).IsEqualTo("alice");
        var otherSpace = space with { Id = "different" };
        await Assert.That(await search.SearchAsync("alice", request with { Space = otherSpace },
            TestContext.Current!.Execution.CancellationToken)).IsEmpty();
    }

    [Test]
    public async Task RejectedResultLeavesStoreUsableForFollowingSearch()
    {
        using var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(Collection, "a", "{\"text\":\"alpha\",\"padding\":\"" + new string('x', 1_024) + "\"}"));
        var position = db.Store.Position;
        var bounded = new SearchEngine(new DatabaseEngine(db.Store, db.Database.Authorization, UnitExecutionOptions.DatabaseLimits(new() { MaxBatchBytes = 128 }), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance), UnitExecutionOptions.QueryExecution());
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => bounded.SearchAsync("root",
            new(db.Partition, Collection, TextPath, "alpha"), TestContext.Current!.Execution.CancellationToken)))!;
        await Assert.That(failure.Code)
            .IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(db.Store.Position).IsEqualTo(position);
        await Assert.That(await new SearchEngine(db.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root", new(db.Partition, Collection, TextPath, "alpha"),
            TestContext.Current!.Execution.CancellationToken)).HasSingleItem();
    }
}
