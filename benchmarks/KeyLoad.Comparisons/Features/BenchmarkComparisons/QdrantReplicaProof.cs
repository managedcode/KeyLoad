using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal sealed record QdrantReplicaResult(string Version, ClusterEvidence Evidence);

internal static class QdrantReplicaProof
{
    private const string ClusterPath = "/cluster";
    private const string CollectionClusterSuffix = "/cluster";
    private const string CollectionsPath = "/collections/";
    private const string ResultField = "result";
    private const string PeersField = "peers";
    private const string PeerIdField = "peer_id";
    private const string LocalShardsField = "local_shards";
    private const string StateField = "state";
    private const string ActiveState = "Active";
    private const string PointsField = "points_count";
    private const string VersionField = "version";
    private const string ConfigField = "config";
    private const string ParamsField = "params";
    private const string ReplicationFactorField = "replication_factor";
    private const string WriteConsistencyField = "write_consistency_factor";
    private const int ReplicatedNodeCount = 3;
    private const int ReplicatedCopyCount = 3;
    private const int PollMilliseconds = 250;
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);

    public static async Task<QdrantReplicaResult> VerifyAsync(HttpClient[] clients, string collection,
        int expectedPoints, ComparisonTopology topology, CancellationToken cancellationToken)
    {
        var required = topology == ComparisonTopology.Replicated ? ReplicatedNodeCount : 1;
        if (clients.Length != required || clients.Any(client => client.BaseAddress is null) ||
            clients.Select(client => client.BaseAddress!.Authority).Distinct(StringComparer.OrdinalIgnoreCase).Count() != required)
        {
            throw new ComparisonFailureException("QdrantInvalidNodeClients");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadinessTimeout);
        while (true)
        {
            try
            {
                var proof = topology == ComparisonTopology.Standalone
                    ? await ReadStandaloneAndDelayAsync(clients[0], collection, expectedPoints, deadline.Token)
                    : await ReadReplicatedAsync(clients, collection, expectedPoints, required, topology, deadline.Token);
                if (proof is not null)
                {
                    return proof;
                }

                if (topology == ComparisonTopology.Standalone)
                {
                    continue;
                }
            }
            catch (HttpRequestException) when (!deadline.IsCancellationRequested) { }
            catch (JsonException) when (!deadline.IsCancellationRequested) { }
            try
            {
                await Task.Delay(PollMilliseconds, deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ComparisonFailureException("QdrantReplicaReadinessTimeout");
            }
        }
    }

    private static async Task<QdrantReplicaResult?> ReadStandaloneAndDelayAsync(HttpClient client, string collection,
        int expectedPoints, CancellationToken token)
    {
        var proof = await ReadStandaloneAsync(client, collection, expectedPoints, token);
        if (proof is null)
        {
            await Task.Delay(PollMilliseconds, token);
        }

        return proof;
    }

    private static async Task<QdrantReplicaResult?> ReadStandaloneAsync(HttpClient client, string collection,
        int expectedPoints, CancellationToken token)
    {
        using var root = await GetJsonAsync(client, "/", token);
        using var collectionInfo = await GetJsonAsync(client, CollectionsPath + collection, token);
        var version = root.RootElement.GetProperty(VersionField).GetString() ?? string.Empty;
        var count = collectionInfo.RootElement.GetProperty(ResultField).GetProperty(PointsField).GetInt32();
        VerifyCollectionPolicy(collectionInfo, ComparisonTopology.Standalone);
        using var cluster = await GetJsonAsync(client, ClusterPath, token);
        QdrantNodeProof.VerifySingleNode(cluster);
        return version.Length != 0 && count == expectedPoints
            ? new(version, new(1, 1, "single node", [$"seeded points={count}; RF1; write_consistency_factor=1"]))
            : null;
    }

    private static async Task<QdrantReplicaResult?> ReadReplicatedAsync(HttpClient[] clients, string collection,
        int expectedPoints, int requiredNodes, ComparisonTopology topology, CancellationToken token)
    {
        var proofs = new List<(string Version, string Peer, string[] Peers, int Copies)>();
        foreach (var client in clients)
        {
            proofs.Add(await ReadNodeAsync(client, collection, expectedPoints, topology, token));
        }

        if (!QdrantNodeProof.Ready(proofs, topology, requiredNodes))
        {
            return null;
        }
        var observations = proofs.SelectMany(proof => proof.Peers.Select(peer =>
            $"peer={peer}; localActiveCopies={proof.Copies}; points={expectedPoints}"))
            .Append(topology == ComparisonTopology.Replicated ? "RF3; write_consistency_factor=2" : "RF1; write_consistency_factor=1")
            .ToArray();
        var evidence = new ClusterEvidence(requiredNodes,
            topology == ComparisonTopology.Replicated ? ReplicatedCopyCount : 1,
            topology == ComparisonTopology.Replicated ? "healthy native cluster" : "single node",
            ImmutableCollectionsMarshal.AsImmutableArray(observations));
        return new(proofs[0].Version, evidence);
    }

    private static async Task<(string Version, string Peer, string[] Peers, int Copies)> ReadNodeAsync(
        HttpClient client, string collection, int expectedPoints, ComparisonTopology topology, CancellationToken token)
    {
        using var root = await GetJsonAsync(client, "/", token);
        var version = root.RootElement.GetProperty(VersionField).GetString() ?? string.Empty;
        using var cluster = await GetJsonAsync(client, ClusterPath, token);
        var clusterResult = cluster.RootElement.GetProperty(ResultField);
        var peerId = clusterResult.GetProperty(PeerIdField).ToString();
        var peers = clusterResult.GetProperty(PeersField).EnumerateObject().Select(peer => peer.Name).Order(StringComparer.Ordinal).ToArray();
        var copies = 1;
        if (topology == ComparisonTopology.Replicated)
        {
            using var collectionInfo = await GetJsonAsync(client, CollectionsPath + collection, token);
            VerifyCollectionPolicy(collectionInfo, topology);
            using var local = await GetJsonAsync(client, CollectionsPath + collection + CollectionClusterSuffix, token);
            var shards = local.RootElement.GetProperty(ResultField).GetProperty(LocalShardsField).EnumerateArray().ToArray();
            if (shards.Length == 0 || shards.Any(shard => shard.GetProperty(StateField).GetString() != ActiveState ||
                    shard.GetProperty(PointsField).GetInt32() != expectedPoints))
            {
                return (version, peerId, peers, 0);
            }

            copies = shards.Length;
        }
        return (version, peerId, peers, copies);
    }

    private static void VerifyCollectionPolicy(JsonDocument collection, ComparisonTopology topology)
    {
        var settings = collection.RootElement.GetProperty(ResultField).GetProperty(ConfigField).GetProperty(ParamsField);
        var expectedReplication = topology == ComparisonTopology.Replicated ? ReplicatedNodeCount : 1;
        var expectedConsistency = topology == ComparisonTopology.Replicated ? 2 : 1;
        if (settings.GetProperty(ReplicationFactorField).GetInt32() != expectedReplication ||
            settings.GetProperty(WriteConsistencyField).GetInt32() != expectedConsistency)
        {
            throw new ComparisonFailureException("QdrantCollectionPolicyMismatch");
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.RelativeOrAbsolute), token);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }
}
