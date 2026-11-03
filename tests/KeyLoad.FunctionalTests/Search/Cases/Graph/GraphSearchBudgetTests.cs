using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class GraphSearchBudgetTests
{
    private const string Tenant = "tenant";
    private const string DatabaseId = "database";
    private const string Domain = "orders";
    private const string OtherPartitionKey = "other-partition";

    [Test]
    public async Task EdgeExhaustionAndPreCancellationReturnNoPartialResultThenAllowHealthySearch()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var root = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var limited = new GraphSearchRequest(new(database.Partition, GraphSearchTestSupport.Documents),
            Retriever: new(GraphSearchTestSupport.Walk(root) with
            {
                MaxEdges = 1
            }));
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
    public async Task CrossPartitionSeedsFailValidation()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents);
        var otherPartition = new PartitionRef(Tenant, DatabaseId, Domain, OtherPartitionKey);
        var invalidSeed = new EntityRef(otherPartition, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var partitionFailure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => engine.GraphSearchAsync(
            "root", new(search, Retriever: new(GraphSearchTestSupport.Walk(invalidSeed))), Token())))!;

        await Assert.That(partitionFailure.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task NullNestedSearchFailsWithTypedValidation()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        var root = GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects,
            GraphSearchTestSupport.Root);
        var request = new GraphSearchRequest(null!, Retriever: new(GraphSearchTestSupport.Walk(root)));

        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).GraphSearchAsync("root", request, Token())))!;

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }

    private static GraphSearchRequest BuildRequest(PartitionRef partition)
    {
        var seed = new EntityRef(partition, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var walk = new GraphWalkSpec(GraphSearchTestSupport.Graph, [seed], MaxDepth: 3, MaxVertices: 10, MaxEdges: 20);
        var search = new SearchRequest(partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle", Limit: 10);
        return new(search, Retriever: new(walk));
    }

    private static CancellationToken Token() => TestContext.Current!.Execution.CancellationToken;
}
