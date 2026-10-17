using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.Search;

/// <summary>Checks graph-search replies against fixed graph-distance expectations.</summary>
internal static class GraphSearchRf3Assertions
{
    internal static async Task AssertHitsAsync(GraphSearchResult result, params (string Id, double Score)[] expected)
    {
        await Assert.That(result.Hits.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var hit = result.Hits[index];
            await Assert.That(hit.Document.Reference.Collection).IsEqualTo(GraphSearchRf3Scenario.Documents);
            await Assert.That(hit.Document.Reference.Id).IsEqualTo(expected[index].Id);
            await Assert.That(hit.Score).IsEqualTo(expected[index].Score);
        }
    }

    internal static async Task AssertExpansionAsync(GraphSearchResult result, string expectedId)
    {
        var expansion = result.Expansion ?? throw new InvalidOperationException("Graph expansion was omitted.");
        await Assert.That(expansion.Completeness).IsEqualTo(GraphExpansionCompleteness.SelectedHits);
        await Assert.That(expansion.Documents.Length).IsEqualTo(1);
        await Assert.That(expansion.Documents[0].Document.Reference.Collection).IsEqualTo(GraphSearchRf3Scenario.Projects);
        await Assert.That(expansion.Documents[0].Document.Reference.Id).IsEqualTo(expectedId);
        await Assert.That(expansion.Documents[0].ShortestHops).IsEqualTo(1);
    }

    internal static async Task AssertEquivalentAsync(GraphSearchResult expected, GraphSearchResult actual)
    {
        var expectedJson = JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options);
        var actualJson = JsonSerializer.SerializeToUtf8Bytes(actual, JsonDefaults.Options);
        await Assert.That(actualJson.AsSpan().SequenceEqual(expectedJson)).IsTrue();
    }
}
