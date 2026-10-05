using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class HybridQualityAssertions
{
    internal static async Task AssertEquivalentAsync(IReadOnlyList<RankedDocument> expected,
        IReadOnlyList<RankedDocument> actual)
    {
        await Assert.That(actual.Select(hit => hit.Document.Reference.Id).ToArray())
            .IsEquivalentTo(expected.Select(hit => hit.Document.Reference.Id).ToArray(), CollectionOrdering.Matching);
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
            await Assert.That(actual[index].Document).IsEquivalentTo(expected[index].Document);
        }
    }

    internal static async Task AssertBranchesEquivalentAsync(ImmutableArray<HybridQualityBranch> expected,
        ImmutableArray<HybridQualityBranch> actual)
    {
        await Assert.That(actual.Select(branch => branch.Name).ToArray())
            .IsEquivalentTo(expected.Select(branch => branch.Name).ToArray(), CollectionOrdering.Matching);
        for (var index = 0; index < expected.Length; index++)
        {
            await AssertEquivalentAsync(expected[index].Hits, actual[index].Hits);
        }
    }

    internal static async Task AssertEligibleAsync(ImmutableArray<HybridQualityBranch> branches,
        HybridQualityQuery query)
    {
        var eligible = query.EligibleIds.ToHashSet(StringComparer.Ordinal);
        foreach (var branch in branches)
        {
            var ids = branch.Hits.Select(hit => hit.Document.Reference.Id).ToArray();
            await Assert.That(ids.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(ids.Length);
            foreach (var id in ids)
            {
                await Assert.That(eligible.Contains(id)).IsTrue();
            }
        }
        if (query.Vector is not null)
        {
            var vector = branches.Single(branch => branch.Kind == GlobalBranchKind.Vector);
            await Assert.That(vector.Hits.Select(hit => hit.Document.Reference.Id).Order(StringComparer.Ordinal).ToArray())
                .IsEquivalentTo(query.EligibleIds.Order(StringComparer.Ordinal).ToArray(), CollectionOrdering.Matching);
        }
    }

    internal static async Task AssertMetricsInRangeAsync(HybridQualityMetrics metrics)
    {
        var values = new[] { metrics.RecallAt5, metrics.RecallAt10, metrics.MeanReciprocalRankAt10,
            metrics.NdcgAt5, metrics.NdcgAt10 };
        foreach (var value in values)
        {
            await Assert.That(double.IsFinite(value) && value is >= 0 and <= 1).IsTrue();
        }
    }

    internal static async Task AssertProductionMatchesWindow32Async(HybridQualityWindowObservation fullWindow,
        IReadOnlyList<RankedDocument> production)
    {
        await Assert.That(fullWindow.FusedIds).IsEquivalentTo(
            production.Select(hit => hit.Document.Reference.Id).ToArray(), CollectionOrdering.Matching);
        await Assert.That(fullWindow.FusedScores.Length).IsEqualTo(production.Count);
        for (var index = 0; index < production.Count; index++)
        {
            await Assert.That(fullWindow.FusedScores[index]).IsEqualTo(production[index].Score);
        }
    }
}
