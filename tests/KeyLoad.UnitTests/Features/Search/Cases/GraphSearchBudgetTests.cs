using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchBudgetTests
{
    private const string Tenant = "tenant";
    private const string DatabaseId = "database";
    private const string Domain = "orders";
    private const string PartitionKey = "customer-1";
    private const string OtherPartitionKey = "other-partition";

    [Test]
    public async Task GraphRequestByteLimitAcceptsExactSizeAndRejectsOneByteLess()
    {
        var partition = new PartitionRef(Tenant, DatabaseId, Domain, PartitionKey);
        var request = BuildRequest(partition);
        var exactBytes = JsonDefaults.Serialize(request).Length;
        using var exact = new TestDatabase(new() { MaxQueryBytes = exactBytes });
        GraphSearchTestSupport.Configure(exact);
        GraphSearchTestSupport.AddPath(exact);
        var accepted = await new SearchEngine(exact.Database).GraphSearchAsync("root", request, Token());

        using var excess = new TestDatabase(new() { MaxQueryBytes = exactBytes - 1 });
        GraphSearchTestSupport.Configure(excess);
        GraphSearchTestSupport.AddPath(excess);
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            new SearchEngine(excess.Database).GraphSearchAsync("root", request, Token())))!;

        await Assert.That(accepted.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo([GraphSearchTestSupport.FirstHit, GraphSearchTestSupport.SecondHit],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task EdgeExhaustionAndPreCancellationReturnNoPartialResultThenAllowHealthySearch()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var engine = new SearchEngine(database.Database);
        var root = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var limited = new GraphSearchRequest(1,
            new(database.Partition, GraphSearchTestSupport.Documents),
            Retriever: new(GraphSearchTestSupport.Walk(root) with { MaxEdges = 1 }));
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            engine.GraphSearchAsync("root", limited, Token())))!;
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => engine.GraphSearchAsync(
            "root", BuildRequest(database.Partition), canceled.Token));

        var healthy = await engine.GraphSearchAsync("root", BuildRequest(database.Partition), Token());

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(healthy.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo([GraphSearchTestSupport.FirstHit, GraphSearchTestSupport.SecondHit],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task UnsupportedVersionAndCrossPartitionSeedsFailValidation()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var engine = new SearchEngine(database.Database);
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents);
        var seed = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var versionFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            "root", new(2, search, Retriever: new(GraphSearchTestSupport.Walk(seed))), Token())))!;
        var otherPartition = new PartitionRef(Tenant, DatabaseId, Domain, OtherPartitionKey);
        var invalidSeed = new EntityRef(otherPartition, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var partitionFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            "root", new(1, search, Retriever: new(GraphSearchTestSupport.Walk(invalidSeed))), Token())))!;

        await Assert.That(versionFailure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(partitionFailure.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task NullNestedSearchFailsWithTypedValidation()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        var root = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects,
            GraphSearchTestSupport.Root);
        var request = new GraphSearchRequest(1, null!, Retriever: new(GraphSearchTestSupport.Walk(root)));

        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            new SearchEngine(database.Database).GraphSearchAsync("root", request, Token())))!;

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }

    private static GraphSearchRequest BuildRequest(PartitionRef partition)
    {
        var seed = new EntityRef(partition, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var walk = new GraphWalkSpec(GraphSearchTestSupport.Graph, [seed], MaxDepth: 3, MaxVertices: 10, MaxEdges: 20);
        var search = new SearchRequest(partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle", Limit: 10);
        return new(1, search, Retriever: new(walk));
    }

    private static CancellationToken Token() => TestContext.Current!.Execution.CancellationToken;
}
