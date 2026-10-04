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
    private const string LocalShardsField = "local_shards";
    private const string ShardCountField = "shard_count";
    private const string ShardIdField = "shard_id";
    private const string StateField = "state";
    private const string ActiveState = "Active";
    private const string PointsField = "points_count";
    private const string VersionField = "version";
    private const string ConfigField = "config";
    private const string ParamsField = "params";
    private const string ReplicationFactorField = "replication_factor";
    private const string WriteConsistencyField = "write_consistency_factor";

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

    internal static QdrantReplicaResult? ReadDisabled(JsonElement root, JsonElement cluster,
        JsonElement collection, int expectedPoints)
    {
        if (cluster.GetProperty(ResultField).GetProperty(StatusField).GetString() != DisabledState)
        {
            throw new ComparisonFailureException(SingleTopologyFailure);
        }
        VerifyCollectionPolicy(collection, ComparisonTopology.Standalone);
        var version = root.GetProperty(VersionField).GetString() ?? string.Empty;
        var count = collection.GetProperty(ResultField).GetProperty(PointsField).GetInt32();
        return version.Length != 0 && count == expectedPoints
            ? new(version, new(1, 1, "single node", [$"seeded points={count}; RF1; write_consistency_factor=1"]))
            : null;
    }

    internal static (string Version, string Peer, string[] Peers, int Copies, int Shard) ReadNode(
        JsonElement root, JsonElement cluster, JsonElement collection, JsonElement local,
        int expectedPoints, ComparisonTopology topology)
    {
        VerifyCollectionPolicy(collection, topology);
        var result = cluster.GetProperty(ResultField);
        if (result.GetProperty(StatusField).GetString() != EnabledState)
        {
            throw new ComparisonFailureException("QdrantNativeTopologyMismatch");
        }
        var version = root.GetProperty(VersionField).GetString() ?? string.Empty;
        var peer = result.GetProperty(PeerIdField).ToString();
        var peers = result.GetProperty(PeersField).EnumerateObject().Select(item => item.Name)
            .Order(StringComparer.Ordinal).ToArray();
        var (copies, shard) = ReadLocalShard(local, peer, expectedPoints);
        return (version, peer, peers, copies, shard);
    }

    private static (int Copies, int Shard) ReadLocalShard(JsonElement local, string peer, int expectedPoints)
    {
        var result = local.GetProperty(ResultField);
        var shards = result.GetProperty(LocalShardsField).EnumerateArray().ToArray();
        if (result.GetProperty(PeerIdField).ToString() != peer ||
            result.GetProperty(ShardCountField).GetInt32() != 1 || shards.Length != 1)
        {
            return (0, -1);
        }
        var shard = shards[0];
        return shard.GetProperty(StateField).GetString() == ActiveState &&
            shard.GetProperty(PointsField).GetInt32() == expectedPoints
            ? (1, shard.GetProperty(ShardIdField).GetInt32()) : (0, -1);
    }

    private static void VerifyCollectionPolicy(JsonElement collection, ComparisonTopology topology)
    {
        var settings = collection.GetProperty(ResultField).GetProperty(ConfigField).GetProperty(ParamsField);
        var nodes = ComparisonTopologies.NodeCount(topology);
        if (settings.GetProperty(ReplicationFactorField).GetInt32() != nodes ||
            settings.GetProperty(WriteConsistencyField).GetInt32() != nodes / 2 + 1)
        {
            throw new ComparisonFailureException("QdrantCollectionPolicyMismatch");
        }
    }

    internal static bool Ready(List<(string Version, string Peer, string[] Peers, int Copies, int Shard)> proofs,
        ComparisonTopology topology, int expected)
    {
        if (expected != ComparisonTopologies.NodeCount(topology) || proofs.Count != expected)
        {
            return false;
        }
        var peers = proofs[0].Peers;
        var distinctPeers = proofs.Select(proof => proof.Peer).Distinct(StringComparer.Ordinal).Count();
        return distinctPeers == expected && peers.Length == expected &&
            peers.Distinct(StringComparer.Ordinal).Count() == expected && proofs.All(proof =>
            proof.Version == proofs[0].Version && proof.Version.Length != 0 && proof.Peers.SequenceEqual(peers) &&
            proof.Peer.Length != 0 && peers.Contains(proof.Peer, StringComparer.Ordinal) &&
            proof.Copies == 1 && proof.Shard >= 0 && proof.Shard == proofs[0].Shard);
    }
}
