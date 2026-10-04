using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchReadCutTests
{
    private const string RootPrincipal = "root";

    [Test]
    public async Task RetrieverAndExpansionShareOneNativeStoreReadGate()
    {
        using var store = new ReadCountingZoneTreeStore();
        var root = new EntityRef(store.Partition, GraphSearchTestSupport.Projects, GraphSearchTestSupport.Root);
        var walk = new GraphWalkSpec(GraphSearchTestSupport.Graph, [root], MaxDepth: 3,
            MaxVertices: 10, MaxEdges: 20);
        var request = new GraphSearchRequest(1,
            new(store.Partition, GraphSearchTestSupport.Documents, Limit: 1),
            Retriever: new(walk),
            Expansion: new(GraphSearchTestSupport.Graph, MaxDepth: 1, MaxVertices: 10, MaxEdges: 20));

        var result = await new SearchEngine(store.Database).GraphSearchAsync(RootPrincipal, request,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(store.ReadCalls).IsEqualTo(1);
        await Assert.That(result.Hits.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEqualTo([GraphSearchTestSupport.FirstHit]);
        await Assert.That(result.Expansion!.Documents.Select(item => item.Document.Reference.Id).ToArray())
            .IsEqualTo([GraphSearchTestSupport.SecondHit]);
    }
}
