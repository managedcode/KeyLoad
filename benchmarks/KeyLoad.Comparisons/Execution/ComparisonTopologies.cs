namespace KeyLoad.Comparisons;

/// <summary>Maps the explicit execution benchmark topology to its real fixed member count.</summary>
public static class ComparisonTopologies
{
    private const int SingleItemCount = 1;
    private const int ThirdContractOrdinal = 3;

    /// <summary>Returns the number of real members expected for a topology.</summary>
    /// <param name="topology">The selected benchmark topology.</param>
    /// <returns>One or three execution members.</returns>
    public static int NodeCount(ComparisonTopology topology) => topology switch
    {
        ComparisonTopology.Standalone => SingleItemCount,
        ComparisonTopology.Replicated => ThirdContractOrdinal,
        _ => throw new ArgumentOutOfRangeException(nameof(topology))
    };

    /// <summary>Selects a execution topology from the required real member count.</summary>
    /// <param name="nodes">One or three execution members.</param>
    /// <returns>The corresponding benchmark topology.</returns>
    public static ComparisonTopology FromNodeCount(int nodes) => nodes switch
    {
        SingleItemCount => ComparisonTopology.Standalone,
        ThirdContractOrdinal => ComparisonTopology.Replicated,
        _ => throw new ArgumentOutOfRangeException(nameof(nodes))
    };
}
