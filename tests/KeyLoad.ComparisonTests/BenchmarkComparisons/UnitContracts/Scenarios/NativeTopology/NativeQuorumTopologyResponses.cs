using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using T = KeyLoad.UnitTests.Features.BenchmarkComparisons.NativeQuorumTopologyTokens;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class NativeQuorumTopologyResponses
{
    internal static JsonObject Cluster(int nodes, int own)
    {
        var peers = new JsonObject();
        foreach (var node in Enumerable.Range(1, nodes))
        {
            peers.Add(node.ToString(CultureInfo.InvariantCulture), new JsonObject { [T.Uri] = $"http://peer{node}:6335" });
        }
        return new()
        {
            [T.Result] = new JsonObject
            {
                [T.Status] = T.Enabled,
                [T.PeerId] = own,
                [T.Peers] = peers
            }
        };
    }

    internal static JsonObject Collection(int nodes) => new()
    {
        [T.Result] = new JsonObject
        {
            [T.Points] = T.PointsCount,
            [T.Config] = new JsonObject
            {
                [T.Params] = new JsonObject
                {
                    [T.Replication] = nodes,
                    [T.Consistency] = nodes / 2 + 1
                }
            }
        }
    };

    internal static JsonObject Local(int own) => new()
    {
        [T.Result] = new JsonObject
        {
            [T.PeerId] = own,
            [T.ShardCount] = 1,
            [T.LocalShards] = new JsonArray(new JsonObject
            {
                [T.ShardId] = 0,
                [T.Points] = T.PointsCount,
                [T.State] = T.Active
            })
        }
    };

    internal static JsonArray Brokers(int nodes) => new(Enumerable.Range(1, nodes)
        .Select(node => (JsonNode)new JsonObject
        {
            [T.Name] = $"rabbit@node{node}",
            [T.Running] = true,
            [T.Type] = T.Disc
        }).ToArray());

    internal static JsonObject Queue(int nodes) => new()
    {
        [T.Name] = T.Queue,
        [T.Type] = T.Quorum,
        [T.Durable] = true,
        [T.Members] = Names(nodes),
        [T.Online] = Names(nodes)
    };

    internal static JsonArray Names(int nodes) => new(Enumerable.Range(1, nodes)
        .Select(node => (JsonNode?)JsonValue.Create($"rabbit@node{node}")).ToArray());

    internal static (string Version, string Peer, string[] Peers, int Copies, int Shard) ReadQdrant(
        int nodes, int own, JsonObject? cluster = null, JsonObject? collection = null, JsonObject? local = null)
    {
        using var rootJson = JsonDocument.Parse(new JsonObject { [T.Version] = T.NativeVersion }.ToJsonString());
        using var clusterJson = JsonDocument.Parse((cluster ?? Cluster(nodes, own)).ToJsonString());
        using var collectionJson = JsonDocument.Parse((collection ?? Collection(nodes)).ToJsonString());
        using var localJson = JsonDocument.Parse((local ?? Local(own)).ToJsonString());
        return QdrantNodeProof.ReadNode(rootJson.RootElement, clusterJson.RootElement, collectionJson.RootElement,
            localJson.RootElement, T.PointsCount, ComparisonTopologies.FromNodeCount(nodes));
    }

    internal static List<(string Version, string Peer, string[] Peers, int Copies, int Shard)> QdrantProofs(int nodes)
        => Enumerable.Range(1, nodes).Select(node => ReadQdrant(nodes, node)).ToList();

    internal static (string Version, ClusterEvidence Evidence)? ReadRabbit(int nodes,
        JsonArray? brokers = null, JsonObject? queue = null, string version = T.BrokerVersion)
    {
        using var nodesJson = JsonDocument.Parse((brokers ?? Brokers(nodes)).ToJsonString());
        using var overviewJson = JsonDocument.Parse(new JsonObject { [T.RabbitVersion] = version }.ToJsonString());
        using var queueJson = JsonDocument.Parse((queue ?? Queue(nodes)).ToJsonString());
        return RabbitQuorumProbe.ReadReady(nodesJson.RootElement, overviewJson.RootElement, queueJson.RootElement,
            T.Queue, ComparisonTopologies.FromNodeCount(nodes));
    }
}
