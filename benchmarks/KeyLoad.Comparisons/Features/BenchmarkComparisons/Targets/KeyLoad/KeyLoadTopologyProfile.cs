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
    private const string Rf1Topology = "1 voter, RF1; quorum 1 of 1; no replica fault tolerance; one physical shard on one host";
    private const string Rf3Topology = "3 voters, RF3, one physical shard; all processes on one host";
    private const string Rf1Acknowledgement = "QuorumProcessDurable; benchmark RF1, one local process acknowledgement; topology recovery and power-loss unqualified";
    private const string Rf3Acknowledgement = "QuorumProcessDurable; process-kill qualified, power-loss unqualified";
    private const string Rf1ReadContract = "strong quorum barrier; requires the only voter; graph returns vertices and edges, projected to IDs";
    private const string Rf3ReadContract = "strong quorum barrier; graph returns vertices and edges, projected to IDs";
    private const string Rf1ClusterState = "One ready benchmark RF1 voter; quorum 1 of 1; no replica fault tolerance; one node-local store on one Docker host";
    private const string Rf3ClusterState = "Three ready RF3 voters; one physical shard; independent node-local stores on one Docker host";

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
            SingleItemCount => (Rf1Topology, Rf1Acknowledgement, Rf1ReadContract),
            _ => (Rf3Topology, Rf3Acknowledgement, Rf3ReadContract)
        };
        return new(TargetName, TargetVersion, topology, acknowledgement, readContract, Transport, Authorization, image);
    }

    internal static string ClusterState(int expectedNodeCount) => ValidateExpectedNodes(expectedNodeCount) switch
    {
        SingleItemCount => Rf1ClusterState,
        _ => Rf3ClusterState
    };

}
