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
    private const string StatusField = "status";
    private const string DisabledState = "disabled";
    private const int PollMilliseconds = 250;
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);

    public static async Task<QdrantReplicaResult> VerifyAsync(HttpClient[] clients, string collection,
        int expectedPoints, ComparisonTopology topology, CancellationToken cancellationToken)
    {
        var required = ComparisonTopologies.NodeCount(topology);
        ValidateClients(clients, topology);
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
            { await Task.Delay(PollMilliseconds, deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new ComparisonFailureException("QdrantReplicaReadinessTimeout"); }
        }
    }

    internal static void ValidateClients(HttpClient[] clients, ComparisonTopology topology)
    {
        var required = ComparisonTopologies.NodeCount(topology);
        if (clients.Length != required || clients.Any(client => client.BaseAddress is null) ||
            clients.Select(client => client.BaseAddress!.Authority).Distinct(StringComparer.OrdinalIgnoreCase).Count() != required)
        {
            throw new ComparisonFailureException("QdrantInvalidNodeClients");
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
        using var cluster = await GetJsonAsync(client, ClusterPath, token);
        QdrantNodeProof.VerifySingleNode(cluster);
        if (cluster.RootElement.GetProperty(ResultField).GetProperty(StatusField).GetString() == DisabledState)
        {
            return QdrantNodeProof.ReadDisabled(root.RootElement, cluster.RootElement, collectionInfo.RootElement, expectedPoints);
        }
        using var local = await GetJsonAsync(client, CollectionsPath + collection + CollectionClusterSuffix, token);
        var proof = QdrantNodeProof.ReadNode(root.RootElement, cluster.RootElement, collectionInfo.RootElement,
            local.RootElement, expectedPoints, ComparisonTopology.Standalone);
        return QdrantNodeProof.Ready([proof], ComparisonTopology.Standalone, 1)
            ? BuildResult([proof], expectedPoints, 1) : null;
    }

    private static async Task<QdrantReplicaResult?> ReadReplicatedAsync(HttpClient[] clients, string collection,
        int expectedPoints, int requiredNodes, ComparisonTopology topology, CancellationToken token)
    {
        var proofs = new List<(string Version, string Peer, string[] Peers, int Copies, int Shard)>();
        foreach (var client in clients)
        {
            proofs.Add(await ReadNodeAsync(client, collection, expectedPoints, topology, token));
        }

        if (!QdrantNodeProof.Ready(proofs, topology, requiredNodes))
        {
            return null;
        }
        return BuildResult(proofs, expectedPoints, requiredNodes);
    }

    private static QdrantReplicaResult BuildResult(
        List<(string Version, string Peer, string[] Peers, int Copies, int Shard)> proofs, int expectedPoints, int nodes)
    {
        var observations = proofs.Select(proof =>
            $"peer={proof.Peer}; shard={proof.Shard}; localActiveCopies={proof.Copies}; points={expectedPoints}")
            .Append($"RF{nodes}; write_consistency_factor={nodes / 2 + 1}")
            .ToArray();
        var evidence = new ClusterEvidence(nodes, nodes, "healthy native cluster",
            ImmutableCollectionsMarshal.AsImmutableArray(observations));
        return new(proofs[0].Version, evidence);
    }

    private static async Task<(string Version, string Peer, string[] Peers, int Copies, int Shard)> ReadNodeAsync(
        HttpClient client, string collection, int expectedPoints, ComparisonTopology topology, CancellationToken token)
    {
        using var root = await GetJsonAsync(client, "/", token);
        using var cluster = await GetJsonAsync(client, ClusterPath, token);
        using var collectionInfo = await GetJsonAsync(client, CollectionsPath + collection, token);
        using var local = await GetJsonAsync(client, CollectionsPath + collection + CollectionClusterSuffix, token);
        return QdrantNodeProof.ReadNode(root.RootElement, cluster.RootElement, collectionInfo.RootElement,
            local.RootElement, expectedPoints, topology);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.RelativeOrAbsolute), token);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
    }
}
