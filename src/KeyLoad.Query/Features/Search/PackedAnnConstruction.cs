namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnConstruction
{
    internal static void Insert(PackedAnnGraph graph, PackedAnnVectors vectors, VectorSpace space,
        int ordinal, int level, ref int entryPoint, ref int maximumLevel, PackedAnnBuildScratch scratch,
        AnnWorkBudget budget)
    {
        if (entryPoint < 0)
        {
            entryPoint = ordinal;
            maximumLevel = level;
            return;
        }
        budget.Charge(vectors.Span(ordinal).Length);
        var similarity = PreparedSimilarity.Create(vectors.Memory(ordinal), space.Metric);
        var current = entryPoint;
        for (var layer = maximumLevel; layer > level; layer--)
        {
            current = PackedAnnLayerSearch.Greedy(graph, vectors, similarity, current, layer,
                space.Dimension, budget);
        }
        var finalLayer = Math.Min(level, maximumLevel);
        for (var layer = finalLayer; layer >= 0; layer--)
        {
            var ef = Math.Min(ordinal, scratch.Layer.Nodes.Length);
            var found = PackedAnnLayerSearch.SearchLayer(graph, vectors, similarity, current, layer,
                ef, space.Dimension, scratch.Layer, budget);
            PackedAnnNeighborSelection.Connect(graph, vectors, space.Metric, space.Dimension,
                ordinal, layer, found, scratch, budget);
            current = scratch.Layer.Nodes[0];
        }
        if (level > maximumLevel)
        {
            entryPoint = ordinal;
            maximumLevel = level;
        }
    }

}
