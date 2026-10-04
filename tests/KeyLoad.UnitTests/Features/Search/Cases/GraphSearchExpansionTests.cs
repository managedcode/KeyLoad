using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchExpansionTests
{
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
                GraphSearchTestSupport.Vertex(database, GraphSearchTestSupport.Documents, GraphSearchTestSupport.SecondHit)
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.Expansion.Documents.Select(item => item.ShortestHops).ToArray())
            .IsEqualTo([1, 2]);
        await Assert.That(result.Expansion.Documents.All(item => !item.Document.Redacted)).IsTrue();
    }
}
