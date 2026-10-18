using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

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
            await Assert.That(hit.Document.Reference).IsEqualTo(scenario.Vertex(GraphSearchClusterScenario.Documents, names[index]));
            await Assert.That(hit.Document.Revision).IsEqualTo(1L);
            await Assert.That(hit.Document.Json).IsEqualTo("{\"name\":\"" + names[index] + "\"}");
            await Assert.That(hit.Document.Redacted).IsFalse();
            await Assert.That(hit.Document.RedactedFields).IsEmpty();
            await Assert.That(hit.Score).IsEqualTo(1d / (61d + index));
        }
    }

    internal static async Task GraphAsync(GraphSearchClusterScenario scenario, KeyLoadClient administrator, CancellationToken token)
    {
        var start = scenario.Vertex(GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.SecondSeed);
        var alpha = scenario.Vertex(GraphSearchClusterScenario.Documents, GraphSearchClusterScenario.Alpha);
        var delta = scenario.Vertex(GraphSearchClusterScenario.Documents, GraphSearchClusterDurableAddition.Delta);
        var context = scenario.Vertex(GraphSearchClusterScenario.Projects, GraphSearchClusterScenario.Context);
        var traversal = await McpCallerAssertions.SdkSuccessAsync(await administrator.TraverseAsync(new(scenario.Partition,
            GraphSearchClusterScenario.Graph, start, GraphSearchClusterScenario.MaxDepth,
            GraphSearchClusterScenario.MaxVertices, GraphSearchClusterScenario.MaxEdges), token));
        await Assert.That(traversal.Vertices).IsEquivalentTo(new[] { start, alpha, delta, context });
        await Assert.That(traversal.Edges).IsEquivalentTo(new[]
        { new EdgeRecord("edge-second-alpha", start, alpha, GraphSearchClusterScenario.Label, "{}", 1),
            new EdgeRecord(GraphSearchClusterDurableAddition.EdgeId, start, delta, GraphSearchClusterScenario.Label, "{}", 1),
            new EdgeRecord("edge-alpha-context", alpha, context, GraphSearchClusterScenario.Label, "{}", 1) });
    }
}
