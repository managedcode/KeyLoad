namespace KeyLoad.Query.Features.Search;

internal sealed class PackedAnnBuildScratch
{
    internal PackedAnnBuildScratch(int count, PackedAnnOptions options, AnnWorkBudget budget)
    {
        var degree = count == 0 ? 0 : checked(options.Connections * 2);
        var ef = Math.Min(count, options.EfConstruction);
        budget.Charge(checked((long)(degree + 1) * 2 + (long)degree * 2));
        Layer = new PackedAnnLayerBuffers(count, ef, false, budget);
        NeighborNodes = new int[degree + 1];
        NeighborScores = new double[degree + 1];
        SelectedNeighbors = new int[degree];
        ConnectionTargets = new int[degree];
        budget.Check();
    }

    internal PackedAnnLayerBuffers Layer { get; }
    internal int[] NeighborNodes { get; }
    internal double[] NeighborScores { get; }
    internal int[] SelectedNeighbors { get; }
    internal int[] ConnectionTargets { get; }
}
