using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnConstructionValueAssertions
{
    internal static async Task AssertEquivalentAsync(TestDatabase database,
        (VectorRecord[] Records, PackedAnnState State) fixture, DistanceMetric metric, int queryOrdinal)
    {
        var state = fixture.State;
        await Assert.That(fixture.Records.Length).IsEqualTo(PackedAnnConstructionValueFixture.RecordCount);
        await Assert.That(state.Count).IsEqualTo(fixture.Records.Length);
        var classValue = PreparedSimilarity.CreatePacked(state.Vectors, queryOrdinal, metric);
        var ownedValue = PreparedPackedSimilarity.Create(state.Vectors, queryOrdinal, metric);
        var query = fixture.Records[queryOrdinal].Values.ToArray();
        var oracle = fixture.Records.Select(record => record.Values.ToArray()).ToArray();
        for (var layer = 0; layer <= state.MaximumLevel; layer++)
        {
            await AssertGreedyAsync(database, state, classValue, ownedValue, layer);
            await AssertLayerAsync(database, state, classValue, ownedValue, layer, query, oracle, metric);
        }
    }

    internal static async Task AssertInvalidOrdinalAsync(TestDatabase database,
        (VectorRecord[] Records, PackedAnnState State) fixture, DistanceMetric metric)
    {
        var classValue = PreparedSimilarity.CreatePacked(fixture.State.Vectors, 0, metric);
        var ownedValue = PreparedPackedSimilarity.Create(fixture.State.Vectors, 0, metric);
        var classBudget = PackedAnnConstructionValueFixture.Budget(database);
        var ownedBudget = PackedAnnConstructionValueFixture.Budget(database);
        var classFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            PackedAnnLayerSearch.Score(fixture.State.Vectors, fixture.State.Count, classValue,
                fixture.State.Space.Dimension, classBudget));
        var ownedFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            PackedAnnLayerSearch.Score(fixture.State.Vectors, fixture.State.Count, ownedValue,
                fixture.State.Space.Dimension, ownedBudget));
        await Assert.That(classFailure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(ownedFailure.Code).IsEqualTo(ErrorCode.Validation);
    }

    internal static async Task AssertZeroTieOrderAsync(TestDatabase database,
        (VectorRecord[] Records, PackedAnnState State) fixture)
    {
        var state = fixture.State;
        var similarity = PreparedPackedSimilarity.Create(state.Vectors, state.EntryPoint,
            DistanceMetric.DotProduct);
        var budget = PackedAnnConstructionValueFixture.Budget(database);
        var buffers = new PackedAnnLayerBuffers(state.Count, state.Count, false, budget);
        var count = PackedAnnLayerSearch.SearchLayer(state.Graph, state.Vectors, similarity,
            state.EntryPoint, 0, state.Count, state.Space.Dimension, buffers, budget);
        await Assert.That(count).IsGreaterThan(0);
        for (var index = 0; index < count; index++)
        {
            await Assert.That(buffers.Scores[index]).IsEqualTo(0d);
            if (index > 0)
            {
                await Assert.That(buffers.Nodes[index]).IsGreaterThan(buffers.Nodes[index - 1]);
            }
        }
    }

    private static async Task AssertGreedyAsync(TestDatabase database, PackedAnnState state,
        PreparedSimilarity classValue, PreparedPackedSimilarity ownedValue, int layer)
    {
        var classBudget = PackedAnnConstructionValueFixture.Budget(database);
        var ownedBudget = PackedAnnConstructionValueFixture.Budget(database);
        var classNode = PackedAnnLayerSearch.Greedy(state.Graph, state.Vectors, classValue,
            state.EntryPoint, layer, state.Space.Dimension, classBudget);
        var ownedNode = PackedAnnLayerSearch.Greedy(state.Graph, state.Vectors, ownedValue,
            state.EntryPoint, layer, state.Space.Dimension, ownedBudget);
        await Assert.That(ownedNode).IsEqualTo(classNode);
        await AssertCountersAsync(classBudget, ownedBudget);
    }

    private static async Task AssertLayerAsync(TestDatabase database, PackedAnnState state,
        PreparedSimilarity classValue, PreparedPackedSimilarity ownedValue, int layer,
        float[] query, float[][] oracle, DistanceMetric metric)
    {
        var classBudget = PackedAnnConstructionValueFixture.Budget(database);
        var ownedBudget = PackedAnnConstructionValueFixture.Budget(database);
        var classBuffers = new PackedAnnLayerBuffers(state.Count, state.Count, false, classBudget);
        var ownedBuffers = new PackedAnnLayerBuffers(state.Count, state.Count, false, ownedBudget);
        var classCount = PackedAnnLayerSearch.SearchLayer(state.Graph, state.Vectors, classValue,
            state.EntryPoint, layer, state.Count, state.Space.Dimension, classBuffers, classBudget);
        var ownedCount = PackedAnnLayerSearch.SearchLayer(state.Graph, state.Vectors, ownedValue,
            state.EntryPoint, layer, state.Count, state.Space.Dimension, ownedBuffers, ownedBudget);
        await Assert.That(ownedCount).IsEqualTo(classCount);
        await Assert.That(MatchingCandidates(classBuffers.Nodes, ownedBuffers.Nodes, classCount)).IsTrue();
        for (var index = 0; index < ownedCount; index++)
        {
            var ordinal = ownedBuffers.Nodes[index];
            var oracleScore = SearchEngine.Similarity(query, oracle[ordinal], metric);
            await Assert.That(BitConverter.DoubleToInt64Bits(ownedBuffers.Scores[index]))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(classBuffers.Scores[index]));
            await Assert.That(BitConverter.DoubleToInt64Bits(ownedBuffers.Scores[index]))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(oracleScore));
        }
        await AssertCountersAsync(classBudget, ownedBudget);
    }

    private static async Task AssertCountersAsync(AnnWorkBudget classBudget, AnnWorkBudget ownedBudget)
    {
        await Assert.That(ownedBudget.WorkUnits).IsEqualTo(classBudget.WorkUnits);
        await Assert.That(ownedBudget.DistanceEvaluations).IsEqualTo(classBudget.DistanceEvaluations);
        await Assert.That(ownedBudget.EdgeVisits).IsEqualTo(classBudget.EdgeVisits);
    }

    private static bool MatchingCandidates(int[] expected, int[] actual, int count)
    {
        if (actual.Length < count || expected.Length < count)
        {
            return false;
        }
        for (var index = 0; index < count; index++)
        {
            if (actual[index] != expected[index])
            {
                return false;
            }
        }
        return true;
    }
}
