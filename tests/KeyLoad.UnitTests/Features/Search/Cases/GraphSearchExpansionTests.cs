using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchExpansionTests
{
    private const string ContextGraph = "graph-search-context";
    private const string ContextEdge = "graph-context-edge";
    private const string ContextFieldGrant = "graph.context.secret";

    [Test]
    public async Task ExpansionProjectsOtherCollectionsSeparatelyAndExcludesSelectedHits()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var expansion = new GraphExpansion(GraphSearchTestSupport.Graph, MaxDepth: 3,
            MaxVertices: 10, MaxEdges: 20);
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle", Limit: 1,
            AllowedIds: [GraphSearchTestSupport.FirstHit]);

        var result = await new SearchEngine(database.Database).GraphSearchAsync("root",
            new(1, search, Expansion: expansion), TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Hits).HasSingleItem();
        await Assert.That(result.Hits[0].Document.Reference.Collection).IsEqualTo(GraphSearchTestSupport.Documents);
        await Assert.That(result.Hits[0].Document.Reference.Id).IsEqualTo(GraphSearchTestSupport.FirstHit);
        await Assert.That(result.Expansion!.Completeness).IsEqualTo(GraphExpansionCompleteness.SelectedHits);
        await Assert.That(result.Expansion.Documents.Select(item => item.Document.Reference).ToArray())
            .IsEquivalentTo([
                GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Middle),
                GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root),
                GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Documents, GraphSearchTestSupport.SecondHit)
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.Expansion.Documents.Select(item => item.ShortestHops).ToArray())
            .IsEquivalentTo([1, 1, 2], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.Expansion.Documents.All(item => !item.Document.Redacted)).IsTrue();
    }

    [Test]
    public async Task ExpansionExcludesEverySelectedHitWhenSeveralHitsSeedTheWalk()
    {
        using var database = new TestDatabase();
        GraphSearchTestSupport.Configure(database);
        GraphSearchTestSupport.AddPath(database);
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle", Limit: 2);
        var request = new GraphSearchRequest(1, search,
            Expansion: new(GraphSearchTestSupport.Graph, MaxDepth: 1, MaxVertices: 10, MaxEdges: 20));

        var result = await new SearchEngine(database.Database).GraphSearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);

        var selected = result.Hits.Select(item => item.Document.Reference).ToHashSet();
        await Assert.That(result.Hits.Select(item => item.Document.Reference.Id).ToArray())
            .IsEquivalentTo([GraphSearchTestSupport.FirstHit, GraphSearchTestSupport.SecondHit],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.Expansion!.Documents.Any(item => selected.Contains(item.Document.Reference))).IsFalse();
        await Assert.That(result.Expansion.Documents.Select(item => item.Document.Reference.Id).ToArray())
            .IsEquivalentTo([GraphSearchTestSupport.Middle, GraphSearchTestSupport.Root],
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExpansionAppliesCurrentFieldProjectionToRelatedDocuments()
    {
        using var database = new TestDatabase();
        database.Configure(GraphSearchTestSupport.Documents, ResourceKind.Collection);
        database.Configure(GraphSearchTestSupport.Projects, ResourceKind.Collection,
            fields: [new("/name", ContextFieldGrant)]);
        database.Configure(GraphSearchTestSupport.Graph, ResourceKind.Graph);
        database.Configure(ContextGraph, ResourceKind.Graph);
        GraphSearchTestSupport.AddPath(database);
        database.Commit(new UpsertEdge(ContextGraph, ContextEdge,
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Documents, GraphSearchTestSupport.FirstHit),
            GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Middle),
            GraphSearchTestSupport.Label));
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(
            GraphSearchTestSupport.Reader, GraphSearchTestSupport.Tenant,
            [
                new(GraphSearchTestSupport.DatabaseScope, GraphSearchTestSupport.Documents,
                    Capability.Query | Capability.DocumentsRead),
                new(GraphSearchTestSupport.DatabaseScope, GraphSearchTestSupport.Projects, Capability.DocumentsRead),
                new(GraphSearchTestSupport.DatabaseScope, ContextGraph, Capability.GraphRead)
            ], [])));
        var search = new SearchRequest(database.Partition, GraphSearchTestSupport.Documents,
            GraphSearchTestSupport.TextField, "needle", Limit: 1,
            AllowedIds: [GraphSearchTestSupport.FirstHit]);
        var request = new GraphSearchRequest(1, search,
            Expansion: new(ContextGraph, MaxDepth: 1, MaxVertices: 10, MaxEdges: 20));

        var result = await new SearchEngine(database.Database).GraphSearchAsync(GraphSearchTestSupport.Reader,
            request, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.Expansion!.Documents).HasSingleItem();
        await Assert.That(result.Expansion.Documents[0].Document.Reference.Collection)
            .IsEqualTo(GraphSearchTestSupport.Projects);
        await Assert.That(result.Expansion.Documents[0].Document.Redacted).IsTrue();
    }
}
