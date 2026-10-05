using System.Runtime.CompilerServices;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal sealed class QdrantVectorOperations(HttpClient client, HttpClient[] nodes, string collection,
    string image, ComparisonTopology topology) : IAsyncDisposable
{
    private bool created;
    private int loaded;
    private VectorIndexKind kind;
    internal TargetProfile Profile { get; private set; } = new("Qdrant", "unverified", "unverified", "unverified",
        "native cosine vectors; bounded numeric predicate metadata", "HTTP JSON", "Aspire API key", image);
    private string Path => "/collections/" + collection;
    private string ReadSuffix => QdrantNativePolicy.QuerySuffix(topology);
    private string WriteSuffix => "?wait=true" + QdrantNativePolicy.SeedSuffix(topology);

    internal async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken token)
    {
        if (created) throw new InvalidOperationException("Qdrant vector collection already initialized.");
        QdrantReplicaProof.ValidateClients(nodes, topology);
        var count = ComparisonTopologies.NodeCount(topology);
        created = true;
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, Path, new
        {
            vectors = new { size = 128, distance = "Cosine" }, shard_number = 1,
            replication_factor = count, write_consistency_factor = count / 2 + 1,
            hnsw_config = new { m = 0 }, optimizers_config = new { indexing_threshold = 0 }
        }, token);
        var batch = new List<VectorDocument>(64);
        await foreach (var document in documents.WithCancellation(token))
        {
            batch.Add(document);
            if (batch.Count == 64) { await WriteBatchAsync(batch, token); loaded += batch.Count; batch.Clear(); }
        }
        if (batch.Count > 0) { await WriteBatchAsync(batch, token); loaded += batch.Count; }
        var proof = await QdrantReplicaProof.VerifyAsync(nodes, collection, loaded, topology, token);
        Profile = Profile with { Version = proof.Version, Cluster = proof.Evidence,
            Topology = QdrantNativePolicy.TopologyLabel(topology), WriteAcknowledgement = QdrantNativePolicy.WriteContract(topology) };
        return loaded;
    }

    private async Task WriteBatchAsync(List<VectorDocument> batch, CancellationToken token)
    {
        var points = batch.Select(document => new
        {
            id = (long)document.Number + 1, vector = document.Embedding.ToArray(),
            payload = new { id = document.Id, number = document.Number, document = document.Payload,
                filtered = document.Number % 100 == 0, mixed = document.Number % 10 != 9 }
        }).ToArray();
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, Path + "/points" + WriteSuffix, new { points }, token);
    }

    internal async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken token)
    {
        if (loaded != profile.RecordCount || profile.IndexKind is not (VectorIndexKind.Exact or VectorIndexKind.Hnsw))
            throw new ComparisonFailureException("QdrantVectorProfileMismatch");
        kind = profile.IndexKind;
        return await QdrantVectorIndex.BuildAsync(client, nodes, collection, profile, token);
    }

    internal async IAsyncEnumerable<VectorReadback> ReadbackAsync([EnumeratorCancellation] CancellationToken token)
    {
        long? offset = null;
        var observed = 0;
        do
        {
            using var page = await QdrantVectorHttp.SendAsync(client, HttpMethod.Post, Path + "/points/scroll" + ReadSuffix,
                new { limit = 64, offset, with_vector = true, with_payload = true }, token);
            var result = page.RootElement.GetProperty("result");
            var points = result.GetProperty("points");
            if (points.GetArrayLength() > 64) throw new ComparisonFailureException("QdrantVectorReadbackPageLimit");
            foreach (var point in points.EnumerateArray())
            {
                if (++observed > loaded) throw new ComparisonFailureException("QdrantVectorReadbackCount");
                yield return QdrantVectorReadback.Parse(point);
            }
            var next = result.GetProperty("next_page_offset");
            var nextOffset = next.ValueKind == JsonValueKind.Null ? (long?)null : next.GetInt64();
            if (nextOffset is not null && offset is not null && nextOffset <= offset)
                throw new ComparisonFailureException("QdrantVectorReadbackCursor");
            offset = nextOffset;
        } while (offset is not null);
    }

    internal async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken token)
    {
        if (query.Length != 128 || topK != 10) throw new ArgumentOutOfRangeException(nameof(topK));
        using var response = await QdrantVectorHttp.SendAsync(client, HttpMethod.Post, Path + "/points/query" + ReadSuffix,
            QdrantVectorQueries.Request(query, topK, kind, mode), token);
        var points = response.RootElement.GetProperty("result").GetProperty("points");
        if (points.GetArrayLength() > topK) throw new ComparisonFailureException("QdrantVectorResultLimit");
        return points.EnumerateArray().Select(point => new VectorNeighbor(
            point.GetProperty("payload").GetProperty("id").GetString()!, 1d - point.GetProperty("score").GetDouble())).ToArray();
    }

    internal async Task<string> ExplainAsync(VectorQueryMode mode, CancellationToken token)
    {
        using var native = await QdrantVectorHttp.SendAsync(client, HttpMethod.Get, Path, null, token);
        return JsonSerializer.Serialize(new { collection = native.RootElement.GetProperty("result"),
            query = QdrantVectorQueries.Parameters(kind, mode), observation = "Native collection configuration; Qdrant has no SQL EXPLAIN endpoint." });
    }

    internal async Task UpdateAsync(VectorUpdate update, CancellationToken token)
    {
        if (QdrantVectorReadback.Number(update.Id) != update.Number || update.Number % 10 != 9)
            throw new ArgumentException("Invalid mixed vector update.", nameof(update));
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, Path + "/points/vectors" + WriteSuffix,
            new { points = new[] { new { id = (long)update.Number + 1, vector = update.Embedding.ToArray() } } }, token);
    }

    internal async Task<VectorReadback?> ReadAsync(string id, CancellationToken token)
    {
        using var response = await QdrantVectorHttp.SendAsync(client, HttpMethod.Post, Path + "/points" + ReadSuffix,
            new { ids = new[] { (long)QdrantVectorReadback.Number(id) + 1 }, with_vector = true, with_payload = true }, token);
        var points = response.RootElement.GetProperty("result");
        if (points.GetArrayLength() > 1) throw new ComparisonFailureException("QdrantVectorReadbackCount");
        return points.GetArrayLength() == 0 ? null : QdrantVectorReadback.Parse(points[0]);
    }

    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Delete, Path, null, deadline.Token);
        created = false;
    }
}
