namespace KeyLoad.Comparisons.Targets;

/// <summary>Fixed topology and acknowledgement descriptions for qualified comparison shapes.</summary>
internal static class KeyLoadTopologyProfile
{
    private const int SingleItemCount = 1;
    private const int ReplicatedMemberCount = 3;
    private const string TargetName = "KeyLoad";
    private const string TargetVersion = "0.1.0-dev";
    private const string Transport = "HTTP JSON";
    private const string Authorization = "authenticated root, all grants";
    private const string SingleTopology = "1 Orleans silo, 1 data copy; independent ZoneTree storage on one host";
    private const string ClusterTopology = "3 Orleans silos, 3 data copies; independent ZoneTree storage on one host";
    private const string SingleAcknowledgement = "ProcessDurable; benchmark requires 1 persisted copy per write receipt; restart and power-loss qualification pending";
    private const string ClusterAcknowledgement = "ProcessDurable; benchmark requires 3 persisted copies per write receipt; restart and power-loss qualification pending";
    private const string SingleReadContract = "authorized database read cut through Orleans; graph returns vertices and edges, projected to IDs";
    private const string ClusterReadContract = "authorized database read cut through Orleans; graph returns vertices and edges, projected to IDs";
    private const string SingleClusterState = "One observed execution Orleans silo and one configured data copy; write receipts separately prove persisted copies";
    private const string ClusterClusterState = "Three observed execution Orleans silos and three configured data copies; write receipts separately prove persisted copies";

    internal static int ValidateExpectedNodes(int expectedNodes)
    {
        if (expectedNodes is not (SingleItemCount or ReplicatedMemberCount))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedNodes));
        }
        return expectedNodes;
    }

    internal static TargetProfile CreateProfile(int expectedNodes, string? image)
    {
        var (topology, acknowledgement, readContract) = ValidateExpectedNodes(expectedNodes) switch
        {
            SingleItemCount => (SingleTopology, SingleAcknowledgement, SingleReadContract),
            _ => (ClusterTopology, ClusterAcknowledgement, ClusterReadContract)
        };
        return new(TargetName, TargetVersion, topology, acknowledgement, readContract, Transport, Authorization, image);
    }

    internal static string ClusterState(int expectedNodeCount) => ValidateExpectedNodes(expectedNodeCount) switch
    {
        SingleItemCount => SingleClusterState,
        _ => ClusterClusterState
    };

}
