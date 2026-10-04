using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchOperatorTests
{
    private const string VectorField = "/embedding";
    private const string VectorModel = "graph-search-model";
    private const string VectorVersion = "1";
    private const int FusionConstant = 60;
    private static VectorSpace Space { get; } = new(VectorModel, 2, DistanceMetric.Cosine, VectorModel, VectorVersion);

    [Test]
    public async Task RetrieverUsesShortestHopsAndDoesNotAmplifyConvergingPathsOrCycles()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var request = new GraphSearchRequest(1,
            new(database.Partition, GraphSearchTestSupport.Documents, FusionConstant: FusionConstant),
            Retriever: new(GraphSearchTestSupport.Walk(database,
                GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root))));

        var result = await new SearchEngine(database.Database).GraphSearchAsync(
            "root", request, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEqualTo([GraphSearchTestSupport.FirstHit, GraphSearchTestSupport.SecondHit]);
        await Assert.That(result.Hits.Select(hit => hit.Score).ToArray())
            .IsEqualTo([1d / (FusionConstant + 1), 1d / (FusionConstant + 2)]);
        await Assert.That(result.Expansion).IsNull();
    }

    [Test]
    public async Task ScopeTraversesIntermediateCollectionsAndAppliesEndpointAllowlistOnlyToHits()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        GraphSearchTestSupport.PersistReader(database);
        var scope = new GraphScope(GraphSearchTestSupport.Walk(database,
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root)));
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle", Limit: 10,
            AllowedIds: [GraphSearchTestSupport.FirstHit]);

        var result = await new SearchEngine(database.Database).GraphSearchAsync(
            GraphSearchTestSupport.Reader, new(1, search, Scope: scope), TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Hits).HasSingleItem();
        await Assert.That(result.Hits[0].Document.Reference.Id).IsEqualTo(GraphSearchTestSupport.FirstHit);
    }

    [Test]
    public async Task TextVectorAndGraphBranchesMatchIndependentWeightedRrfRanks()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        database.Commit(
            new PutVector(GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit, VectorField,
                [1, 0], Space, 1),
            new PutVector(GraphSearchTestSupport.Documents, GraphSearchTestSupport.SecondHit, VectorField,
                [0, 1], Space, 1));
        var walk = GraphSearchTestSupport.Walk(database,
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root));
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle", VectorField, [1, 0], Space,
            Limit: 10, FusionConstant: FusionConstant);

        var result = await new SearchEngine(database.Database).GraphSearchAsync("root",
            new(1, search, Retriever: new(walk)), TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEqualTo([GraphSearchTestSupport.FirstHit, GraphSearchTestSupport.SecondHit]);
        await Assert.That(result.Hits.Select(hit => hit.Score).ToArray())
            .IsEqualTo([3d / (FusionConstant + 1), 3d / (FusionConstant + 2)]);
    }

    [Test]
    public async Task EmptyLabelsAuthorizeFilterAndProduceNoReachableEndpoints()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database, protectLabels: true);
        GraphSearchTestSupport.AddPath(database);
        GraphSearchTestSupport.PersistReader(database);
        var scope = new GraphScope(GraphSearchTestSupport.Walk(database,
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root))
        { Labels = System.Collections.Immutable.ImmutableArray<string>.Empty });
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle");

        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            new SearchEngine(database.Database).GraphSearchAsync(GraphSearchTestSupport.Reader,
                new(1, search, Scope: scope), TestContext.Current!.Execution.CancellationToken)))!;

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }
}
