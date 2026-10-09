namespace KeyLoad.Comparisons.Targets;

internal static class QdrantNativePolicy
{
    private const string SpaceSeparator = " ";
    private const string TopologyLabelRFText = "; RF";
    private const string TopologyLabelWCFText = ", WCF";
    private const string TopologyLabelQuorumText = "; quorum ";
    private const string TopologyLabelOfText = " of ";
    private const string WriteContractWaitTrueWriteConsistencyFactorText = "wait=true; write_consistency_factor=";
    private const string WriteContractOfRFText = " of RF";

    private const int SingleItemCount = 1;
    private const string ExactTrueConsistencyMajoritySeededCollectionStaticDuringQueriesContractText = "exact=true; consistency=majority; seeded collection static during queries";
    private const string ExactTrueSeededCollectionStaticDuringQueriesToken = "exact=true; seeded collection static during queries";

    private const string StrongOrdering = "&ordering=strong";
    private const string MajorityRead = "?consistency=majority";

    internal static object CreateCollection(int dimensions, ComparisonTopology topology)
    {
        const string CosineToken = "Cosine";
        const int SingleItemCount = 1;
        const int MajorityDivisor = 2;
        const int MajorityVoteOffset = 1;
        const int NoObservedItems = 0;

        var nodes = ComparisonTopologies.NodeCount(topology);
        return new
        {
            vectors = new { size = dimensions, distance = CosineToken },
            shard_number = SingleItemCount,
            replication_factor = nodes,
            write_consistency_factor = nodes / MajorityDivisor + MajorityVoteOffset,
            hnsw_config = new { m = NoObservedItems }
        };
    }

    internal static string SeedSuffix(ComparisonTopology topology)
        => ComparisonTopologies.NodeCount(topology) > SingleItemCount ? StrongOrdering : string.Empty;

    internal static string QuerySuffix(ComparisonTopology topology)
        => ComparisonTopologies.NodeCount(topology) > SingleItemCount ? MajorityRead : string.Empty;

    internal static string TopologyLabel(ComparisonTopology topology)
    {
        const string NativeNodeToken = "native node";
        const string NativePeersToken = "native peers";

        const int SingleItemCount = 1;
        const string NoReplicaFaultToleranceToken = "; no replica fault tolerance";
        const int SingleNodeTopology = 1;
        const int MajorityDivisor = 2;
        const int MajorityVoteOffset = 1;

        var nodes = ComparisonTopologies.NodeCount(topology);
        var availability = nodes switch
        {
            SingleItemCount => NoReplicaFaultToleranceToken,
            _ => string.Empty
        };
        var native = nodes == SingleNodeTopology ? NativeNodeToken : NativePeersToken;
        return $"{nodes}{SpaceSeparator}{native}{TopologyLabelRFText}{nodes}{TopologyLabelWCFText}{nodes / MajorityDivisor + MajorityVoteOffset}{TopologyLabelQuorumText}{nodes / MajorityDivisor + MajorityVoteOffset}{TopologyLabelOfText}{nodes}{availability}";
    }

    internal static string WriteContract(ComparisonTopology topology)
    {
        const int MajorityDivisor = 2;
        const int MajorityVoteOffset = 1;
        const int SingleNodeTopology = 1;
        const string SeedOrderingStrongToken = "; seed ordering=strong";

        var nodes = ComparisonTopologies.NodeCount(topology);
        return $"{WriteContractWaitTrueWriteConsistencyFactorText}{nodes / MajorityDivisor + MajorityVoteOffset}{WriteContractOfRFText}{nodes}"
            + (nodes > SingleNodeTopology ? SeedOrderingStrongToken : string.Empty);
    }

    internal static string ReadContract(ComparisonTopology topology)
        => ComparisonTopologies.NodeCount(topology) > SingleItemCount
            ? ExactTrueConsistencyMajoritySeededCollectionStaticDuringQueriesContractText
            : ExactTrueSeededCollectionStaticDuringQueriesToken;
}
