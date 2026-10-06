using System.Runtime.InteropServices;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class KeyLoadTarget
{
    private const string ObserveCopiesAsyncReadyIncarnationText = ": ready; incarnation ";
    private const string ObserveCopiesAsyncAppliedText = "; applied ";
    private const string ObserveCopiesAsyncLeaderText = "; leader ";

    private const int SingleItemCount = 1;

    private const string MissingPeers = "KeyLoadExpectedRealEndpointsRequired";
    private const string InvalidCluster = "KeyLoadClusterReceiptMissing";
    private const string Rf3Required = "KeyLoadRf3Required";
    private const string BenchmarkTopologyMismatch = "KeyLoadBenchmarkTopologyMismatch";
    private async Task ObserveCopiesAsync(CancellationToken cancellationToken)
    {
        if (peerClients.Length != expectedNodeCount
            || peerClients.Select(peer => peer.BaseAddress).Distinct().Count() != expectedNodeCount)
        {
            throw new ComparisonFailureException(MissingPeers);
        }

        var readers = peerClients.Select(peer => new KeyLoadClient(peer, credential, clientOptions)).ToArray();
        var initial = await ReadStatusesAsync(readers, cancellationToken);
        var required = initial.Max(status => status.Applied);
        using var deadline = new ComparisonCancellationSource(timeProvider, cancellationToken);
        deadline.CancelAfter(lifecycle.ReadinessTimeout);
        var receipts = initial;
        while (receipts.Any(status => status.Applied < required))
        {
            await Task.Delay(lifecycle.KeyLoadReadinessPollInterval, timeProvider, deadline.Token);
            receipts = await ReadStatusesAsync(readers, deadline.Token);
        }
        ValidateStatuses(receipts);
        var description = KeyLoadTopologyProfile.ClusterState(expectedNodeCount);
        Profile = Profile with
        {
            Topology = description,
            Cluster = new(expectedNodeCount, expectedNodeCount, description, ImmutableCollectionsMarshal.AsImmutableArray(receipts.Select(status =>
                $"{status.NodeId}{ObserveCopiesAsyncReadyIncarnationText}{status.Incarnation}{ObserveCopiesAsyncAppliedText}{status.Applied}{ObserveCopiesAsyncLeaderText}{status.Leader}")
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
        const int SingleItemCount = 1;

        if (receipts.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count() != expectedNodeCount
            || receipts.Select(status => status.Incarnation).Distinct().Count() != SingleItemCount
            || receipts.Select(status => status.Leader).Distinct(StringComparer.Ordinal).Count() != SingleItemCount
            || receipts.Any(status => !status.RoutingReady || status.Voters != expectedNodeCount || string.IsNullOrWhiteSpace(status.Leader)
                || status.Durability != DurabilityProfile.QuorumProcessDurable))
        {
            throw new ComparisonFailureException(InvalidCluster);
        }
    }
}
