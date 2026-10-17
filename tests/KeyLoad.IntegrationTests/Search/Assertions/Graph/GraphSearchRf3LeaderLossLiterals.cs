using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GraphSearchRf3LeaderLossLiterals
{
    internal static async Task HitsAsync(GraphSearchRf3Scenario scenario, GraphSearchResult result, bool added)
    {
        var names = added ? new[] { "alpha", "beta", "delta", "gamma" } : new[] { "alpha", "beta", "gamma" };
        await Assert.That(result.Hits.Length).IsEqualTo(names.Length);
        await Assert.That(result.Expansion).IsNull();
        for (var index = 0; index < names.Length; index++)
        {
            var hit = result.Hits[index];
            await Assert.That(hit.Document.Reference).IsEqualTo(scenario.Vertex(GraphSearchRf3Scenario.Documents, names[index]));
            await Assert.That(hit.Document.Revision).IsEqualTo(1L);
            await Assert.That(hit.Document.Json).IsEqualTo("{\"name\":\"" + names[index] + "\"}");
            await Assert.That(hit.Document.Redacted).IsFalse();
            await Assert.That(hit.Document.RedactedFields).IsEmpty();
            await Assert.That(hit.Score).IsEqualTo(1d / (61d + index));
        }
    }

    internal static async Task GraphAsync(GraphSearchRf3Scenario scenario, KeyLoadClient administrator, CancellationToken token)
    {
        var start = scenario.Vertex(GraphSearchRf3Scenario.Projects, GraphSearchRf3Scenario.SecondSeed);
        var alpha = scenario.Vertex(GraphSearchRf3Scenario.Documents, GraphSearchRf3Scenario.Alpha);
        var delta = scenario.Vertex(GraphSearchRf3Scenario.Documents, GraphSearchRf3DurableAddition.Delta);
        var context = scenario.Vertex(GraphSearchRf3Scenario.Projects, GraphSearchRf3Scenario.Context);
        var traversal = await McpCallerAssertions.SdkSuccessAsync(await administrator.TraverseAsync(new(scenario.Partition,
            GraphSearchRf3Scenario.Graph, start, GraphSearchRf3Scenario.MaxDepth,
            GraphSearchRf3Scenario.MaxVertices, GraphSearchRf3Scenario.MaxEdges), token));
        await Assert.That(traversal.Vertices).IsEquivalentTo(new[] { start, alpha, delta, context });
        await Assert.That(traversal.Edges).IsEquivalentTo(new[]
        { new EdgeRecord("edge-second-alpha", start, alpha, GraphSearchRf3Scenario.Label, "{}", 1),
            new EdgeRecord(GraphSearchRf3DurableAddition.EdgeId, start, delta, GraphSearchRf3Scenario.Label, "{}", 1),
            new EdgeRecord("edge-alpha-context", alpha, context, GraphSearchRf3Scenario.Label, "{}", 1) });
    }
}
