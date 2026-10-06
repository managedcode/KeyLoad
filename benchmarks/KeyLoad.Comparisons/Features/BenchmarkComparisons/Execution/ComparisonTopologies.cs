namespace KeyLoad.Comparisons;

/// <summary>Maps the explicit native benchmark topology to its real fixed member count.</summary>
public static class ComparisonTopologies
{
    private const int SingleItemCount = 1;
    private const int PairMemberCount = 2;
    private const int ThirdContractOrdinal = 3;

    /// <summary>Returns the number of real members expected for a topology.</summary>
    /// <param name="topology">The selected benchmark topology.</param>
    /// <returns>One, two or three native members.</returns>
    public static int NodeCount(ComparisonTopology topology) => topology switch
    {
        ComparisonTopology.Standalone => SingleItemCount,
        ComparisonTopology.TwoNode => PairMemberCount,
        ComparisonTopology.Replicated => ThirdContractOrdinal,
        _ => throw new ArgumentOutOfRangeException(nameof(topology))
    };

    /// <summary>Selects a native topology from the required real member count.</summary>
    /// <param name="nodes">One, two or three native members.</param>
    /// <returns>The corresponding benchmark topology.</returns>
    public static ComparisonTopology FromNodeCount(int nodes) => nodes switch
    {
        SingleItemCount => ComparisonTopology.Standalone,
        PairMemberCount => ComparisonTopology.TwoNode,
        ThirdContractOrdinal => ComparisonTopology.Replicated,
        _ => throw new ArgumentOutOfRangeException(nameof(nodes))
    };
}
