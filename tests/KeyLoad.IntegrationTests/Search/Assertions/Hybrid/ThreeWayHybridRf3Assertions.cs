namespace KeyLoad.IntegrationTests.Features.Search;

internal static class ThreeWayHybridRf3Assertions
{
    internal const double Tolerance = 0.000000000001;

    internal static async Task AssertWeightedResultAsync(GraphSearchResult result, bool restricted)
    {
        var expected = IndependentRrf(restricted);
        await Assert.That(result.Hits.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(result.Hits[index].Document.Reference.Collection)
                .IsEqualTo(ThreeWayHybridRf3Scenario.Documents);
            await Assert.That(result.Hits[index].Document.Reference.Id).IsEqualTo(expected[index].Id);
            await Assert.That(result.Hits[index].Score).IsEqualTo(expected[index].Score).Within(Tolerance);
        }
    }

    internal static async Task AssertExpansionAsync(GraphSearchResult result)
    {
        var expansion = result.Expansion ?? throw new InvalidOperationException("The expected context was omitted.");
        await Assert.That(expansion.Completeness).IsEqualTo(GraphExpansionCompleteness.SelectedHits);
        await Assert.That(expansion.Documents.Length).IsEqualTo(1);
        await Assert.That(expansion.Documents[0].Document.Reference.Collection)
            .IsEqualTo(ThreeWayHybridRf3Scenario.Projects);
        await Assert.That(expansion.Documents[0].Document.Reference.Id).IsEqualTo("context");
        await Assert.That(expansion.Documents[0].ShortestHops).IsEqualTo(1);
        await Assert.That(result.Hits.Any(hit => hit.Document.Reference.Id == "context")).IsFalse();
    }

    internal static (string Id, double Score)[] IndependentRrf(bool restricted)
    {
        var scope = restricted ? new[] { "a", "b", "c" } : new[] { "a", "b", "c", "d" };
        var allowed = restricted ? new[] { "b", "c", "d" } : scope;
        var eligible = scope.Intersect(allowed, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        Add(["a", "b", "c"], ThreeWayHybridRf3Scenario.TextWeight);
        Add(["b", "a", "c", "d"], ThreeWayHybridRf3Scenario.VectorWeight);
        Add(["d", "c", "b"], ThreeWayHybridRf3Scenario.GraphWeight);
        return scores.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => (item.Key, item.Value)).ToArray();

        void Add(string[] order, double weight)
        {
            var rank = 0;
            foreach (var id in order.Where(eligible.Contains))
            {
                scores[id] = scores.GetValueOrDefault(id) + weight / (ThreeWayHybridRf3Scenario.FusionConstant + ++rank);
            }
        }
    }
}
