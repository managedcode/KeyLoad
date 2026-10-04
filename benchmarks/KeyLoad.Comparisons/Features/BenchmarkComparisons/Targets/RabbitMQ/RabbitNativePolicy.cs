namespace KeyLoad.Comparisons.Targets;

internal static class RabbitNativePolicy
{
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
        var nodes = ComparisonTopologies.NodeCount(topology);
        var availability = nodes switch
        {
            1 => "; no replica fault tolerance",
            2 => "; no single-node-loss availability",
            _ => string.Empty
        };
        return $"{nodes} connected native broker nodes; {nodes}-member quorum queue; quorum {nodes / 2 + 1} of {nodes}{availability}";
    }
}
