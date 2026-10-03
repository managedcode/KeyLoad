using System.Runtime.InteropServices;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class KeyLoadTarget
{
    private const string MissingPeers = "KeyLoadExpectedRealEndpointsRequired";
    private const string InvalidCluster = "KeyLoadClusterReceiptMissing";
    private const string Rf3Required = "KeyLoadRf3Required";
    private const string BenchmarkTopologyMismatch = "KeyLoadBenchmarkTopologyMismatch";
    private const string TargetName = "KeyLoad";
    private const string TargetVersion = "0.1.0-dev";
    private const string Transport = "HTTP JSON";
    private const string Authorization = "authenticated root, all grants";
    private const string Rf1Topology = "1 voter, RF1; quorum 1 of 1; no replica fault tolerance; one physical shard on one host";
    private const string Rf2Topology = "2 voters, RF2; quorum 2 of 2; no single-voter-loss availability; one physical shard; all processes on one host";
    private const string Rf3Topology = "3 voters, RF3, one physical shard; all processes on one host";
    private const string Rf1Acknowledgement = "QuorumProcessDurable; benchmark RF1, one local process acknowledgement; topology recovery and power-loss unqualified";
    private const string Rf2Acknowledgement = "QuorumProcessDurable; benchmark RF2 requires both process acknowledgements; topology recovery and power-loss unqualified";
    private const string Rf3Acknowledgement = "QuorumProcessDurable; process-kill qualified, power-loss unqualified";
    private const string Rf1ReadContract = "strong quorum barrier; requires the only voter; graph returns vertices and edges, projected to IDs";
    private const string Rf2ReadContract = "strong quorum barrier; requires both voters; graph returns vertices and edges, projected to IDs";
    private const string Rf3ReadContract = "strong quorum barrier; graph returns vertices and edges, projected to IDs";
    private const string Rf1ClusterState = "One ready benchmark RF1 voter; quorum 1 of 1; no replica fault tolerance; one node-local store on one Docker host";
    private const string Rf2ClusterState = "Two ready benchmark RF2 voters; quorum 2 of 2; no single-voter-loss availability; independent node-local stores on one Docker host";
    private const string Rf3ClusterState = "Three ready RF3 voters; one physical shard; independent node-local stores on one Docker host";

    private static int ValidateExpectedNodes(int expectedNodes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedNodes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedNodes, 3);
        return expectedNodes;
    }

    private static TargetProfile CreateProfile(int expectedNodes, string? image)
    {
        var (topology, acknowledgement, readContract) = ValidateExpectedNodes(expectedNodes) switch
        {
            1 => (Rf1Topology, Rf1Acknowledgement, Rf1ReadContract),
            2 => (Rf2Topology, Rf2Acknowledgement, Rf2ReadContract),
            _ => (Rf3Topology, Rf3Acknowledgement, Rf3ReadContract)
        };
        return new(TargetName, TargetVersion, topology, acknowledgement, readContract, Transport, Authorization, image);
    }

    private string ClusterState() => expectedNodeCount switch
    {
        1 => Rf1ClusterState,
        2 => Rf2ClusterState,
        _ => Rf3ClusterState
    };

    private async Task ObserveCopiesAsync(CancellationToken cancellationToken)
    {
        if (peerClients.Length != expectedNodeCount
            || peerClients.Select(peer => peer.BaseAddress).Distinct().Count() != expectedNodeCount)
        {
            throw new ComparisonFailureException(MissingPeers);
        }

        var readers = peerClients.Select(peer => new KeyLoadClient(peer, credential)).ToArray();
        var initial = await ReadStatusesAsync(readers, cancellationToken);
        var required = initial.Max(status => status.Applied);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var receipts = initial;
        while (receipts.Any(status => status.Applied < required))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            receipts = await ReadStatusesAsync(readers, deadline.Token);
        }
        ValidateStatuses(receipts);
        var description = ClusterState();
        Profile = Profile with
        {
            Topology = description,
            Cluster = new(expectedNodeCount, expectedNodeCount, description, ImmutableCollectionsMarshal.AsImmutableArray(receipts.Select(status =>
                $"{status.NodeId}: ready; incarnation {status.Incarnation}; applied {status.Applied}; leader {status.Leader}")
                .Concat(admissionObservations).ToArray()))
        };
    }

    private static async Task<NodeStatus[]> ReadStatusesAsync(KeyLoadClient[] readers, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(readers.Select(reader => reader.StatusAsync(cancellationToken)));
        return results.Select(result => KeyLoadClientResults.Success(result)).ToArray();
    }

    private void ValidateStatuses(NodeStatus[] receipts)
    {
        if (receipts.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count() != expectedNodeCount
            || receipts.Select(status => status.Incarnation).Distinct().Count() != 1
            || receipts.Select(status => status.Leader).Distinct(StringComparer.Ordinal).Count() != 1
            || receipts.Any(status => !status.RoutingReady || status.Voters != expectedNodeCount || string.IsNullOrWhiteSpace(status.Leader)
                || status.Durability != DurabilityProfile.QuorumProcessDurable))
        {
            throw new ComparisonFailureException(InvalidCluster);
        }
    }
}
