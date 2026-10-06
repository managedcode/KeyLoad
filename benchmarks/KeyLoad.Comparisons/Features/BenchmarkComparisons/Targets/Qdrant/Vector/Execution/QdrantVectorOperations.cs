using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class QdrantVectorOperations(HttpClient client, HttpClient[] nodes, string collection,
    string image, ComparisonTopology topology, IOptions<NativeComparisonExecutionOptions> executionOptions,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions) : IAsyncDisposable
{

    private bool created;
    private int loaded;
    private VectorIndexKind kind;
    internal TargetProfile Profile { get; private set; } = new(QdrantVectorProtocol.TargetName, QdrantVectorProtocol.Unverified, QdrantVectorProtocol.Unverified, QdrantVectorProtocol.Unverified,
        QdrantVectorProtocol.InitialReadContract, QdrantVectorProtocol.Transport, QdrantVectorProtocol.Authentication, image);
    private string Path => QdrantVectorProtocol.CollectionPrefix + collection;
    private string ReadSuffix => QdrantNativePolicy.QuerySuffix(topology);
    private string WriteSuffix => QdrantVectorProtocol.WaitForApply + QdrantNativePolicy.SeedSuffix(topology);

    internal async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken token)
    {
        if (created)
        {
            throw new InvalidOperationException(QdrantVectorProtocol.AlreadyInitialized);
        }
        QdrantReplicaProof.ValidateClients(nodes, topology);
        var count = ComparisonTopologies.NodeCount(topology);
        created = true;
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, Path, new
        {
            vectors = new { size = QdrantVectorProtocol.VectorDimensions, distance = QdrantVectorProtocol.CosineMetric },
            shard_number = QdrantVectorProtocol.SingleShard,
            replication_factor = count,
            write_consistency_factor = count / QdrantVectorProtocol.QuorumDivisor + QdrantVectorProtocol.MajorityIncrement,
            hnsw_config = new { m = QdrantVectorProtocol.DisabledIndex },
            optimizers_config = new { indexing_threshold = QdrantVectorProtocol.DisabledIndex }
        }, executionOptions, token);
        var batch = new List<VectorDocument>(executionOptions.Value.WriteBatchCapacity);
        await foreach (var document in documents.WithCancellation(token))
        {
            batch.Add(document);
            if (batch.Count == executionOptions.Value.WriteBatchCapacity)
            { await WriteBatchAsync(batch, token); loaded += batch.Count; batch.Clear(); }
        }
        if (batch.Count > QdrantVectorProtocol.EmptyCount)
        { await WriteBatchAsync(batch, token); loaded += batch.Count; }
        var proof = await QdrantReplicaProof.VerifyAsync(clients: nodes, collection: collection, expectedPoints: loaded, topology: topology,
            cancellationToken: token, lifecycleOptions: lifecycleOptions);
        Profile = Profile with
        {
            Version = proof.Version,
            Cluster = proof.Evidence,
            Topology = QdrantNativePolicy.TopologyLabel(topology),
            WriteAcknowledgement = QdrantNativePolicy.WriteContract(topology)
        };
        return loaded;
    }

    private async Task WriteBatchAsync(List<VectorDocument> batch, CancellationToken token)
    {
        var points = batch.Select(document => new
        {
            id = (long)document.Number + QdrantVectorProtocol.PointOrdinalOffset,
            vector = document.Embedding.ToArray(),
            payload = new
            {
                id = document.Id,
                number = document.Number,
                document = document.Payload,
                filtered = document.Number % QdrantVectorProtocol.FilterDivisor == QdrantVectorProtocol.EmptyCount,
                mixed = document.Number % QdrantVectorProtocol.MutableDivisor != QdrantVectorProtocol.MutableRemainder
            }
        }).ToArray();
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, Path + QdrantVectorProtocol.PointsPath + WriteSuffix, new { points }, executionOptions, token);
    }

    internal async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken token)
    {
        if (loaded != profile.RecordCount || profile.IndexKind is not (VectorIndexKind.Exact or VectorIndexKind.Hnsw))
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.ProfileMismatch);
        }
        kind = profile.IndexKind;
        return await QdrantVectorIndex.BuildAsync(client, nodes, collection, profile, executionOptions, token);
    }

    internal async IAsyncEnumerable<VectorReadback> ReadbackAsync([EnumeratorCancellation] CancellationToken token)
    {
        long? offset = null;
        var observed = QdrantVectorProtocol.EmptyCount;
        do
        {
            using var page = await QdrantVectorHttp.SendAsync(client, HttpMethod.Post, Path + QdrantVectorProtocol.ScrollPath + ReadSuffix,
                new { limit = executionOptions.Value.ReadbackBatchCapacity, offset, with_vector = true, with_payload = true }, executionOptions, token);
            var result = page.RootElement.GetProperty(QdrantVectorProtocol.Result);
            var points = result.GetProperty(QdrantVectorProtocol.Points);
            if (points.GetArrayLength() > executionOptions.Value.ReadbackBatchCapacity)
            {
                throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackPageLimit);
            }
            foreach (var point in points.EnumerateArray())
            {
                if (++observed > loaded)
                {
                    throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackCount);
                }
                yield return QdrantVectorReadback.Parse(point);
            }
            var next = result.GetProperty(QdrantVectorProtocol.NextPageOffset);
            var nextOffset = next.ValueKind == JsonValueKind.Null ? (long?)null : next.GetInt64();
            if (nextOffset is not null && offset is not null && nextOffset <= offset)
            {
                throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackCursor);
            }
            offset = nextOffset;
        } while (offset is not null);
    }

    internal async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken token)
    {
        if (query.Length != QdrantVectorProtocol.VectorDimensions || topK != QdrantVectorProtocol.ResultCount)
        {
            throw new ArgumentOutOfRangeException(nameof(topK));
        }
        using var response = await QdrantVectorHttp.SendAsync(client, HttpMethod.Post, Path + QdrantVectorProtocol.QueryPath + ReadSuffix,
            QdrantVectorQueries.Request(query, topK, kind, mode), executionOptions, token);
        var points = response.RootElement.GetProperty(QdrantVectorProtocol.Result).GetProperty(QdrantVectorProtocol.Points);
        if (points.GetArrayLength() > topK)
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.ResultLimit);
        }
        return points.EnumerateArray().Select(point => new VectorNeighbor(
            point.GetProperty(QdrantVectorProtocol.Payload).GetProperty(QdrantVectorProtocol.Identifier).GetString()!, QdrantVectorProtocol.CosineUnit - point.GetProperty(QdrantVectorProtocol.Score).GetDouble()))
            .OrderBy(neighbor => neighbor.Distance).ThenBy(neighbor => neighbor.Id, StringComparer.Ordinal).ToArray();
    }

    internal async Task<string> ExplainAsync(VectorQueryMode mode, CancellationToken token)
    {
        using var native = await QdrantVectorHttp.SendAsync(client, HttpMethod.Get, Path, null, executionOptions, token);
        return JsonSerializer.Serialize(new
        {
            collection = native.RootElement.GetProperty(QdrantVectorProtocol.Result),
            query = QdrantVectorQueries.Evidence(kind, mode),
            observation = QdrantVectorProtocol.PlanObservation
        });
    }

    internal async Task UpdateAsync(VectorUpdate update, CancellationToken token)
    {
        if (QdrantVectorReadback.Number(update.Id) != update.Number || update.Number % QdrantVectorProtocol.MutableDivisor != QdrantVectorProtocol.MutableRemainder)
        {
            throw new ArgumentException(QdrantVectorProtocol.InvalidUpdate, nameof(update));
        }
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, Path + QdrantVectorProtocol.UpdatePath + WriteSuffix,
            new { points = new[] { new { id = (long)update.Number + QdrantVectorProtocol.PointOrdinalOffset, vector = update.Embedding.ToArray() } } }, executionOptions, token);
    }

    internal async Task<VectorReadback?> ReadAsync(string id, CancellationToken token)
    {
        using var response = await QdrantVectorHttp.SendAsync(client, HttpMethod.Post, Path + QdrantVectorProtocol.PointsPath + ReadSuffix,
            new { ids = new[] { (long)QdrantVectorReadback.Number(id) + QdrantVectorProtocol.PointOrdinalOffset }, with_vector = true, with_payload = true }, executionOptions, token);
        var points = response.RootElement.GetProperty(QdrantVectorProtocol.Result);
        if (points.GetArrayLength() > QdrantVectorProtocol.SingleShard)
        {
            throw new ComparisonFailureException(QdrantVectorProtocol.ReadbackCount);
        }
        return points.GetArrayLength() == QdrantVectorProtocol.EmptyCount ? null : QdrantVectorReadback.Parse(points[QdrantVectorProtocol.FirstElementIndex]);
    }

    public async ValueTask DisposeAsync()
    {
        if (!created)
        {
            return;
        }
        using var deadline = new CancellationTokenSource(executionOptions.Value.CleanupTimeout);
        using var receipt = await QdrantVectorHttp.SendAsync(client, HttpMethod.Delete, Path, null, executionOptions, deadline.Token);
        created = false;
    }
}
