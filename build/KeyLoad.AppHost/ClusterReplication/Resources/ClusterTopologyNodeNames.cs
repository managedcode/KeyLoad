namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Creates native silo resource names for configured and explicitly qualified comparison topologies.</summary>
internal static class ClusterTopologyNodeNames
{
    private const string FunctionalEphemeralOwnershipRequired = "Functional topology requires explicit ephemeral ownership.";
    private const string NodeNamePrefix = "node";
    private const int FirstNodeOrdinal = 1;
    private const int DefaultSiloCount = 3;
    private const int MinimumBenchmarkNodes = 1;
    private const int DistributedBenchmarkNodes = 3;

    internal static string[] Read(int? benchmarkNodeCount, int? functionalNodeCount, bool ephemeral, int siloCount = DefaultSiloCount)
    {
        if (functionalNodeCount is not null && !ephemeral)
        {
            throw new ArgumentException(FunctionalEphemeralOwnershipRequired, nameof(functionalNodeCount));
        }
        if (functionalNodeCount is not null && benchmarkNodeCount is not null)
        {
            throw new ArgumentException(FunctionalEphemeralOwnershipRequired, nameof(functionalNodeCount));
        }
        if (benchmarkNodeCount is not null && benchmarkNodeCount.Value is not (MinimumBenchmarkNodes or DistributedBenchmarkNodes))
        {
            throw new ArgumentOutOfRangeException(nameof(benchmarkNodeCount));
        }
        var selectedCount = benchmarkNodeCount ?? functionalNodeCount ?? siloCount;
        if (selectedCount < FirstNodeOrdinal)
        {
            throw new ArgumentOutOfRangeException(nameof(siloCount));
        }
        return Enumerable.Range(FirstNodeOrdinal, selectedCount).Select(index => NodeNamePrefix + index.ToString(
            System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }
}
