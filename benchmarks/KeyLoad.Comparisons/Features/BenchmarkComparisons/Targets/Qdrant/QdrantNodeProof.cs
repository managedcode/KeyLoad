using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantNodeProof
{
    private const string ReadDisabledSeededPointsText = "seeded points=";
    private const string ReadDisabledRF1WriteConsistencyFactorText = "; RF1; write_consistency_factor=1";

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
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

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
        if (peers.Length != SingleItemCount || peers[FirstElementIndex] != result.GetProperty(PeerIdField).ToString())
        {
            throw new ComparisonFailureException(SingleTopologyFailure);
        }
    }

    internal static QdrantReplicaResult? ReadDisabled(JsonElement root, JsonElement cluster,
        JsonElement collection, int expectedPoints)
    {
        const int NoItems = 0;
        const int SingleItemCount = 1;
        const string SingleNodeToken = "single node";

        if (cluster.GetProperty(ResultField).GetProperty(StatusField).GetString() != DisabledState)
        {
            throw new ComparisonFailureException(SingleTopologyFailure);
        }
        VerifyCollectionPolicy(collection, ComparisonTopology.Standalone);
        var version = root.GetProperty(VersionField).GetString() ?? string.Empty;
        var count = collection.GetProperty(ResultField).GetProperty(PointsField).GetInt32();
        return version.Length != NoItems && count == expectedPoints
            ? new(version, new(SingleItemCount, SingleItemCount, SingleNodeToken, [$"{ReadDisabledSeededPointsText}{count}{ReadDisabledRF1WriteConsistencyFactorText}"]))
            : null;
    }

    internal static (string Version, string Peer, string[] Peers, int Copies, int Shard) ReadNode(
        JsonElement root, JsonElement cluster, JsonElement collection, JsonElement local,
        int expectedPoints, ComparisonTopology topology)
    {
        const string QdrantNativeTopologyMismatchDetail = "QdrantNativeTopologyMismatch";

        VerifyCollectionPolicy(collection, topology);
        var result = cluster.GetProperty(ResultField);
        if (result.GetProperty(StatusField).GetString() != EnabledState)
        {
            throw new ComparisonFailureException(QdrantNativeTopologyMismatchDetail);
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
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;
        const int MissingItemIndex = -1;
        const int FirstElementIndex = 0;

        var result = local.GetProperty(ResultField);
        var shards = result.GetProperty(LocalShardsField).EnumerateArray().ToArray();
        if (result.GetProperty(PeerIdField).ToString() != peer ||
            result.GetProperty(ShardCountField).GetInt32() != SingleItemCount || shards.Length != SingleItemCount)
        {
            return (NoObservedItems, MissingItemIndex);
        }
        var shard = shards[FirstElementIndex];
        return shard.GetProperty(StateField).GetString() == ActiveState &&
            shard.GetProperty(PointsField).GetInt32() == expectedPoints
            ? (SingleItemCount, shard.GetProperty(ShardIdField).GetInt32()) : (NoObservedItems, MissingItemIndex);
    }

    private static void VerifyCollectionPolicy(JsonElement collection, ComparisonTopology topology)
    {
        const int MajorityDivisor = 2;
        const int MajorityVoteOffset = 1;
        const string QdrantCollectionPolicyMismatchDetail = "QdrantCollectionPolicyMismatch";

        var settings = collection.GetProperty(ResultField).GetProperty(ConfigField).GetProperty(ParamsField);
        var nodes = ComparisonTopologies.NodeCount(topology);
        if (settings.GetProperty(ReplicationFactorField).GetInt32() != nodes ||
            settings.GetProperty(WriteConsistencyField).GetInt32() != nodes / MajorityDivisor + MajorityVoteOffset)
        {
            throw new ComparisonFailureException(QdrantCollectionPolicyMismatchDetail);
        }
    }

    internal static bool Ready(List<(string Version, string Peer, string[] Peers, int Copies, int Shard)> proofs,
        ComparisonTopology topology, int expected)
    {
        const int FirstElementIndex = 0;
        const int NoItems = 0;
        const int SingleNodeTopology = 1;
        const int NoObservedItems = 0;

        if (expected != ComparisonTopologies.NodeCount(topology) || proofs.Count != expected)
        {
            return false;
        }
        var peers = proofs[FirstElementIndex].Peers;
        var distinctPeers = proofs.Select(proof => proof.Peer).Distinct(StringComparer.Ordinal).Count();
        return distinctPeers == expected && peers.Length == expected &&
            peers.Distinct(StringComparer.Ordinal).Count() == expected && proofs.All(proof =>
            proof.Version == proofs[FirstElementIndex].Version && proof.Version.Length != NoItems && proof.Peers.SequenceEqual(peers) &&
            proof.Peer.Length != NoItems && peers.Contains(proof.Peer, StringComparer.Ordinal) &&
            proof.Copies == SingleNodeTopology && proof.Shard >= NoObservedItems && proof.Shard == proofs[FirstElementIndex].Shard);
    }
}
