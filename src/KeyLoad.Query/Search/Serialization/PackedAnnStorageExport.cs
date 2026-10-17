namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnStorageExport
{
    private const int First = 0;
    private const int Step = 1;
    private const int ObjectAllowance = 128;
    private const int ReferenceBytes = 8;

    internal static long Reservation(PackedAnnState state, int ordinal, AnnWorkBudget budget)
    {
        var layers = state.Levels[ordinal] + Step;
        var bytes = checked(ObjectAllowance + PackedAnnReservations.Array(sizeof(float), state.Space.Dimension)
            + PackedAnnReservations.Array(ReferenceBytes, layers));
        for (var layer = First; layer < layers; layer++)
        {
            var count = state.Graph.NeighborCount(ordinal, layer, budget);
            bytes = checked(bytes + PackedAnnReservations.Array(sizeof(int), count));
        }
        return bytes;
    }

    internal static PackedAnnStorageNode Node(PackedAnnState state, int ordinal, AnnWorkBudget budget)
    {
        budget.Charge(state.Space.Dimension);
        var layers = new int[state.Levels[ordinal] + Step][];
        for (var layer = First; layer < layers.Length; layer++)
        {
            var count = state.Graph.NeighborCount(ordinal, layer, budget);
            layers[layer] = Neighbors(state.Graph, ordinal, layer, count, budget);
        }
        return new(ordinal, state.Ids[ordinal], state.Revisions[ordinal], state.Levels[ordinal],
            state.Vectors.Span(ordinal).ToArray(), layers);
    }

    private static int[] Neighbors(PackedAnnGraph graph, int ordinal, int layer, int count, AnnWorkBudget budget)
    {
        var result = new int[count];
        for (var offset = First; offset < count; offset++)
        {
            budget.Charge(Step);
            result[offset] = graph.Neighbor(ordinal, layer, offset);
        }
        return result;
    }
}
