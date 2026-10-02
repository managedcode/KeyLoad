using System.Runtime.InteropServices;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class KeyLoadTarget
{
    private const string MissingPeers = "KeyLoadThreeRealEndpointsRequired";
    private const string InvalidCluster = "KeyLoadClusterReceiptMissing";
    private const string ClusterState = "Three ready RF3 voters; one physical shard; independent node-local stores on one Docker host";

    private async Task ObserveCopiesAsync(CancellationToken cancellationToken)
    {
        if (peerClients.Length != 3 || peerClients.Select(peer => peer.BaseAddress).Distinct().Count() != 3)
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
        Profile = Profile with
        {
            Topology = ClusterState,
            Cluster = new(3, 3, ClusterState, ImmutableCollectionsMarshal.AsImmutableArray(receipts.Select(status =>
                $"{status.NodeId}: ready; incarnation {status.Incarnation}; applied {status.Applied}; leader {status.Leader}").ToArray()))
        };
    }

    private static async Task<NodeStatus[]> ReadStatusesAsync(KeyLoadClient[] readers, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(readers.Select(reader => reader.StatusAsync(cancellationToken)));
        return results.Select(result => KeyLoadClientResults.Success(result)).ToArray();
    }

    private static void ValidateStatuses(NodeStatus[] receipts)
    {
        if (receipts.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count() != 3
            || receipts.Select(status => status.Incarnation).Distinct().Count() != 1
            || receipts.Select(status => status.Leader).Distinct(StringComparer.Ordinal).Count() != 1
            || receipts.Any(status => !status.RoutingReady || status.Voters != 3 || string.IsNullOrWhiteSpace(status.Leader)
                || status.Durability != DurabilityProfile.QuorumProcessDurable))
        {
            throw new ComparisonFailureException(InvalidCluster);
        }
    }
}
