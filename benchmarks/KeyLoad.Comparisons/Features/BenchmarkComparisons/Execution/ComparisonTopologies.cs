namespace KeyLoad.Comparisons;

/// <summary>Maps the explicit native benchmark topology to its real fixed member count.</summary>
public static class ComparisonTopologies
{
    /// <summary>Returns the number of real members expected for a topology.</summary>
    /// <param name="topology">The selected benchmark topology.</param>
    /// <returns>One, two or three native members.</returns>
    public static int NodeCount(ComparisonTopology topology) => topology switch
    {
        ComparisonTopology.Standalone => 1,
        ComparisonTopology.TwoNode => 2,
        ComparisonTopology.Replicated => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(topology))
    };

    /// <summary>Selects a native topology from the required real member count.</summary>
    /// <param name="nodes">One, two or three native members.</param>
    /// <returns>The corresponding benchmark topology.</returns>
    public static ComparisonTopology FromNodeCount(int nodes) => nodes switch
    {
        1 => ComparisonTopology.Standalone,
        2 => ComparisonTopology.TwoNode,
        3 => ComparisonTopology.Replicated,
        _ => throw new ArgumentOutOfRangeException(nameof(nodes))
    };
}
