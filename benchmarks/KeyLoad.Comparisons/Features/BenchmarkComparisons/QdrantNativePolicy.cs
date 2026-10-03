namespace KeyLoad.Comparisons.Targets;

internal static class QdrantNativePolicy
{
    private const string StrongOrdering = "&ordering=strong";
    private const string MajorityRead = "?consistency=majority";

    internal static object CreateCollection(int dimensions, ComparisonTopology topology)
    {
        var nodes = ComparisonTopologies.NodeCount(topology);
        return new
        {
            vectors = new { size = dimensions, distance = "Cosine" },
            shard_number = 1,
            replication_factor = nodes,
            write_consistency_factor = nodes / 2 + 1,
            hnsw_config = new { m = 0 }
        };
    }

    internal static string SeedSuffix(ComparisonTopology topology)
        => ComparisonTopologies.NodeCount(topology) > 1 ? StrongOrdering : string.Empty;

    internal static string QuerySuffix(ComparisonTopology topology)
        => ComparisonTopologies.NodeCount(topology) > 1 ? MajorityRead : string.Empty;

    internal static string TopologyLabel(ComparisonTopology topology)
    {
        var nodes = ComparisonTopologies.NodeCount(topology);
        var availability = nodes switch
        {
            1 => "; no replica fault tolerance",
            2 => "; no single-node-loss availability",
            _ => string.Empty
        };
        var native = nodes == 1 ? "native node" : "native peers";
        return $"{nodes} {native}; RF{nodes}, WCF{nodes / 2 + 1}; quorum {nodes / 2 + 1} of {nodes}{availability}";
    }

    internal static string WriteContract(ComparisonTopology topology)
    {
        var nodes = ComparisonTopologies.NodeCount(topology);
        return $"wait=true; write_consistency_factor={nodes / 2 + 1} of RF{nodes}"
            + (nodes > 1 ? "; seed ordering=strong" : string.Empty);
    }

    internal static string ReadContract(ComparisonTopology topology)
        => ComparisonTopologies.NodeCount(topology) > 1
            ? "exact=true; consistency=majority; seeded collection static during queries"
            : "exact=true; seeded collection static during queries";
}
