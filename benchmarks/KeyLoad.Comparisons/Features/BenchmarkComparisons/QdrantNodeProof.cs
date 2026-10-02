using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantNodeProof
{
    private const string ResultField = "result";
    private const string PeersField = "peers";
    private const string PeerIdField = "peer_id";
    private const string StatusField = "status";
    private const string DisabledState = "disabled";
    private const string EnabledState = "enabled";
    private const string SingleTopologyFailure = "QdrantSingleTopologyMismatch";

    internal static void VerifySingleNode(JsonDocument cluster)
    {
        var result = cluster.RootElement.GetProperty(ResultField);
        var status = result.GetProperty(StatusField).GetString();
        if (status == DisabledState)
        {
            return;
        }

        if (status != EnabledState)
        {
            throw new ComparisonFailureException(SingleTopologyFailure);
        }

        var peers = result.GetProperty(PeersField).EnumerateObject().Select(peer => peer.Name).ToArray();
        if (peers.Length != 1 || peers[0] != result.GetProperty(PeerIdField).ToString())
        {
            throw new ComparisonFailureException(SingleTopologyFailure);
        }
    }

    internal static bool Ready(List<(string Version, string Peer, string[] Peers, int Copies)> proofs,
        ComparisonTopology topology, int expected)
    {
        var peers = proofs[0].Peers;
        var distinctPeers = proofs.Select(proof => proof.Peer).Distinct(StringComparer.Ordinal).Count();
        return proofs.Count == expected && distinctPeers == expected && proofs.All(proof =>
            proof.Version == proofs[0].Version && proof.Version.Length != 0 && proof.Peers.SequenceEqual(peers) &&
            peers.Contains(proof.Peer, StringComparer.Ordinal) && proof.Copies > 0) && peers.Length == expected &&
            (topology != ComparisonTopology.Replicated || proofs.All(proof => proof.Copies == 1));
    }
}
