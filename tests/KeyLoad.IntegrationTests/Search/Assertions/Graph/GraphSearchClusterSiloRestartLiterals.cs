using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Search;

internal static class GraphSearchClusterSiloRestartLiterals
{
    internal static async Task HitsAsync(GraphSearchClusterScenario scenario, GraphSearchResult result, bool added)
    {
        var names = added ? new[] { "alpha", "beta", "delta", "gamma" } : new[] { "alpha", "beta", "gamma" };
        await Assert.That(result.Hits.Length).IsEqualTo(names.Length);
        await Assert.That(result.Expansion).IsNull();
        for (var index = 0; index < names.Length; index++)
        {
            var hit = result.Hits[index];
            await Assert.That(hit.Document.Reference).IsEqualTo(scenario.StoredVertex(GraphSearchClusterScenario.Documents, names[index]));
            await Assert.That(hit.Document.Revision).IsEqualTo(1L);
            await Assert.That(hit.Document.Json).IsEqualTo("{\"name\":\"" + names[index] + "\"}");
            await Assert.That(hit.Document.Redacted).IsFalse();
            await Assert.That(hit.Document.RedactedFields).IsEmpty();
            await Assert.That(hit.Score).IsEqualTo(1d / (61d + index));
        }
    }

    internal static async Task GraphAsync(GraphSearchClusterScenario scenario, KeyLoadClient administrator, CancellationToken token)
    {
        var start = scenario.StoredVertex(GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.SecondSeed);
        var alpha = scenario.StoredVertex(GraphSearchClusterScenario.Documents, GraphSearchClusterScenario.Alpha);
        var delta = scenario.StoredVertex(GraphSearchClusterScenario.Documents, GraphSearchClusterDurableAddition.Delta);
        var context = scenario.StoredVertex(GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.Context);
        var origin = scenario.Vertex(GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.SecondSeed);
        var traversal = await McpCallerAssertions.SdkSuccessAsync(await administrator.TraverseAsync(new(scenario.Partition,
            GraphSearchClusterScenario.Graph, origin, GraphSearchClusterScenario.MaxDepth,
            GraphSearchClusterScenario.MaxVertices, GraphSearchClusterScenario.MaxEdges), token));
        await Assert.That(traversal.Vertices).IsEquivalentTo(new[] { start, alpha, delta, context });
        await Assert.That(traversal.Edges).IsEquivalentTo(new[]
        { new EdgeRecord("edge-second-alpha", start, alpha, GraphSearchClusterScenario.Label, "{}", 1),
            new EdgeRecord(GraphSearchClusterDurableAddition.EdgeId, start, delta, GraphSearchClusterScenario.Label, "{}", 1),
            new EdgeRecord("edge-alpha-context", alpha, context, GraphSearchClusterScenario.Label, "{}", 1) });
    }
}
