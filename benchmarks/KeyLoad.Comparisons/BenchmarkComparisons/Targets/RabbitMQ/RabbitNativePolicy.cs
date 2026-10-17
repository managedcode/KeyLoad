namespace KeyLoad.Comparisons.Targets;

internal static class RabbitNativePolicy
{
    private const string TopologyLabelConnectedNativeBrokerNodesText = " connected native broker nodes; ";
    private const string TopologyLabelMemberQuorumQueueQuorumText = "-member quorum queue; quorum ";
    private const string TopologyLabelOfText = " of ";

    private const string QueueTypeArgument = "x-queue-type";
    private const string InitialGroupSizeArgument = "x-quorum-initial-group-size";
    private const string QuorumType = "quorum";

    internal static Dictionary<string, object?> QueueArguments(ComparisonTopology topology) => new()
    {
        [QueueTypeArgument] = QuorumType,
        [InitialGroupSizeArgument] = ComparisonTopologies.NodeCount(topology)
    };

    internal static string TopologyLabel(ComparisonTopology topology)
    {
        const int SingleItemCount = 1;
        const string NoReplicaFaultToleranceToken = "; no replica fault tolerance";
        const int MajorityDivisor = 2;
        const int MajorityVoteOffset = 1;

        var nodes = ComparisonTopologies.NodeCount(topology);
        var availability = nodes switch
        {
            SingleItemCount => NoReplicaFaultToleranceToken,
            _ => string.Empty
        };
        return $"{nodes}{TopologyLabelConnectedNativeBrokerNodesText}{nodes}{TopologyLabelMemberQuorumQueueQuorumText}{nodes / MajorityDivisor + MajorityVoteOffset}{TopologyLabelOfText}{nodes}{availability}";
    }
}
