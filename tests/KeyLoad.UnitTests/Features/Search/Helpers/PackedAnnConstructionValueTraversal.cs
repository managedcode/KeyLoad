using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnConstructionValueTraversal
{
    internal static PackedAnnConstructionValueObservation Measure(TestDatabase database,
        (VectorRecord[] Records, PackedAnnState State) fixture, DistanceMetric metric)
    {
        var state = fixture.State;
        var queryOrdinal = state.EntryPoint;
        var similarity = PreparedPackedSimilarity.Create(state.Vectors, queryOrdinal, metric);
        var budget = PackedAnnConstructionValueFixture.Budget(database);
        var capacity = state.Count;
        var buffers = new PackedAnnLayerBuffers(state.Count, capacity, false, budget);
        _ = Run(state, similarity, buffers, budget, 2);
        _ = Run(state, similarity, buffers, budget, 2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var traversed = Run(state, similarity, buffers, budget, 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return traversed with { AllocatedBytes = allocated };
    }

    private static PackedAnnConstructionValueObservation Run(PackedAnnState state,
        PreparedPackedSimilarity similarity, PackedAnnLayerBuffers buffers, AnnWorkBudget budget,
        int repetitions)
    {
        var greedy = -1;
        var candidates = 0;
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            for (var layer = 0; layer <= state.MaximumLevel; layer++)
            {
                greedy = PackedAnnLayerSearch.Greedy(state.Graph, state.Vectors, similarity,
                    state.EntryPoint, layer, state.Space.Dimension, budget);
                candidates = PackedAnnLayerSearch.SearchLayer(state.Graph, state.Vectors, similarity,
                    state.EntryPoint, layer, state.Count, state.Space.Dimension, buffers, budget);
            }
        }
        return new(greedy, candidates, budget.WorkUnits, budget.DistanceEvaluations,
            budget.EdgeVisits, 0);
    }
}
