namespace KeyLoad.Query.Features.Search;

internal sealed class PackedAnnBuildScratch
{
    private const int EmptyElementCount = 0;
    private const int BidirectionalDegreeMultiplier = 2;
    private const int SingleWorkUnit = 1;
    private const int AdjacentElementOffset = 1;

    internal PackedAnnBuildScratch(int count, int connections, int efConstruction, AnnWorkBudget budget)
    {
        var degree = count == EmptyElementCount ? EmptyElementCount : checked(connections * BidirectionalDegreeMultiplier);
        var ef = Math.Min(count, efConstruction);
        budget.Charge(checked((long)(degree + SingleWorkUnit) * BidirectionalDegreeMultiplier + (long)degree * BidirectionalDegreeMultiplier));
        Layer = new PackedAnnLayerBuffers(count, ef, false, budget);
        NeighborNodes = new int[degree + AdjacentElementOffset];
        NeighborScores = new double[degree + AdjacentElementOffset];
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
