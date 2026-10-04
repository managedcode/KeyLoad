using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnOwnedSimilarityAssertions
{
    internal static async Task VerifyPackedScoresAsync(TestDatabase database, VectorSpace space,
        DistanceMetric metric, VectorRecord[] records)
    {
        var budget = PackedAnnIndexTestSupport.Budget(database);
        var options = new PackedAnnOptions();
        var layout = PackedAnnAdmission.Create(space, records, options, budget);
        var packed = PackedAnnVectors.Copy(records, layout, budget);
        var originals = records.Select(record => record.Values.ToArray()).ToArray();
        var expected = ExactScores(metric, originals);
        var prepared = Enumerable.Range(0, records.Length)
            .Select(ordinal => PreparedSimilarity.CreatePacked(packed, ordinal, metric)).ToArray();
        MutateSource(records);
        await AssertScoresAsync(packed, prepared, expected);
        PoisonSource(records);
        await AssertValidationAsync(() => PackedAnnVectors.Copy(records, layout,
            PackedAnnIndexTestSupport.Budget(database)));
        await AssertScoresAsync(packed, prepared, expected);
    }

    internal static async Task AssertGraphAdjacencyAsync(PackedAnnState state, AnnWorkBudget budget)
    {
        for (var source = 0; source < state.Count; source++)
        {
            for (var layer = 0; layer <= state.Levels[source]; layer++)
            {
                var count = state.Graph.NeighborCount(source, layer, budget);
                var seen = new HashSet<int>();
                await Assert.That(count).IsLessThanOrEqualTo(state.Graph.Degree(layer));
                for (var offset = 0; offset < count; offset++)
                {
                    var neighbor = state.Graph.Neighbor(source, layer, offset);
                    await Assert.That(neighbor).IsNotEqualTo(source);
                    await Assert.That(state.Levels[neighbor]).IsGreaterThanOrEqualTo(layer);
                    await Assert.That(seen.Add(neighbor)).IsTrue();
                }
            }
        }
    }

    internal static async Task AssertValidationAsync(Action operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
    }

    private static double[][] ExactScores(DistanceMetric metric, float[][] vectors)
        => vectors.Select(query => vectors.Select(candidate => SearchEngine.Similarity(query, candidate, metric))
            .ToArray()).ToArray();

    private static void MutateSource(VectorRecord[] records)
    {
        var borrowed = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(records[1].Values)!;
        borrowed[0] = 17f;
    }

    private static void PoisonSource(VectorRecord[] records)
    {
        var borrowed = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(records[2].Values)!;
        borrowed[0] = float.NaN;
    }

    private static async Task AssertScoresAsync(PackedAnnVectors packed,
        PreparedSimilarity[] prepared, double[][] expected)
    {
        for (var query = 0; query < prepared.Length; query++)
        {
            for (var candidate = 0; candidate < prepared.Length; candidate++)
            {
                var actual = prepared[query].ScorePacked(packed, candidate);
                await Assert.That(actual).IsEqualTo(expected[query][candidate]);
            }
        }
    }
}
