using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnAdjacencyAssertions
{
    internal static int[][][] Snapshot(PackedAnnState state, AnnWorkBudget budget)
    {
        var adjacency = new int[state.Count][][];
        for (var source = 0; source < state.Count; source++)
        {
            adjacency[source] = new int[state.Levels[source] + 1][];
            for (var layer = 0; layer < adjacency[source].Length; layer++)
            {
                var count = state.Graph.NeighborCount(source, layer, budget);
                var neighbors = new int[count];
                for (var offset = 0; offset < count; offset++)
                { neighbors[offset] = state.Graph.Neighbor(source, layer, offset); }
                adjacency[source][layer] = neighbors;
            }
        }
        return adjacency;
    }

    internal static async Task AssertSameAsync(int[][][] expected, int[][][] actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var source = 0; source < expected.Length; source++)
        {
            await Assert.That(actual[source].Length).IsEqualTo(expected[source].Length);
            for (var layer = 0; layer < expected[source].Length; layer++)
            { await Assert.That(actual[source][layer].SequenceEqual(expected[source][layer])).IsTrue(); }
        }
    }
}
