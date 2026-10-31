using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>
/// Qdrant over its REST API: one collection with HNSW and indexing disabled, one shard replicated to every node with a
/// majority <c>write_consistency_factor</c>, upserts with <c>wait=true</c> (plus <c>ordering=strong</c> on a cluster), and
/// <c>points/query</c> with <c>exact: true</c> (plus <c>consistency=majority</c> on a cluster). Calls rotate over the nodes.
/// </summary>
internal sealed class QdrantVectorDriver(BenchmarkConnection connection) : IVectorDriver
{
    private const string CollectionPath = "/collections/benchmark";
    private const string ClusterPath = CollectionPath + "/cluster";
    private const string UpsertPath = CollectionPath + "/points?wait=true";
    private const string StrongOrdering = "&ordering=strong";
    private const string QueryPath = CollectionPath + "/points/query";
    private const string MajorityRead = "?consistency=majority";
    private const string JsonMediaType = "application/json";
    private const string Cosine = "Cosine";
    private const string Completed = "completed";
    private const string Active = "Active";
    private const string ResultField = "result";
    private const string StatusField = "status";
    private const string PointsField = "points";
    private const string PayloadField = "payload";
    private const string IdField = "id";
    private const string LocalShardsField = "local_shards";
    private const string StateField = "state";
    private const string NoLocalShard = "no local shard";
    private const string NoState = "no shard state";
    private const string RequestFailed = "Qdrant ";
    private const string NotCreated = "Qdrant did not create the collection: ";
    private const string NotCompleted = "Qdrant did not complete the upsert: ";
    private const string NotActive = "Qdrant shard is not active on ";
    private const string MissingId = "Qdrant returned a point without its payload id.";
    private const int PointIdBytes = 16;
    private static readonly string[] PayloadFields = [IdField];
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadinessLimit = TimeSpan.FromMinutes(5);
    private readonly HttpClient[] nodes = [.. Enumerable.Range(0, connection.Endpoints.Count).Select(node => connection.CreateHttpClient(node))];
    private readonly string upsertPath = UpsertPath + (connection.Nodes > 1 ? StrongOrdering : string.Empty);
    private readonly string queryPath = QueryPath + (connection.Nodes > 1 ? MajorityRead : string.Empty);
    private int next = -1;

    public int BatchSize => 100;

    private HttpClient Next => nodes[(int)((uint)Interlocked.Increment(ref next) % (uint)nodes.Length)];

    /// <summary>Creates the collection on the first node; on a cluster, waits until every node holds an active replica.</summary>
    public async Task PrepareAsync(int dimensions, CancellationToken token)
    {
        await CreateCollectionAsync(dimensions, token);
        if (nodes.Length > 1)
        {
            foreach (var node in nodes)
            {
                await WaitActiveAsync(node, token);
            }
        }
    }

    public async Task InsertAsync(IReadOnlyList<VectorRecord> vectors, CancellationToken token)
    {
        var points = vectors.Select(vector => new { id = PointId(vector.Id), vector = vector.Values, payload = new { id = vector.Id } }).ToArray();
        using var reply = await SendAsync(Next, HttpMethod.Put, upsertPath, new { points }, token);
        var status = reply.RootElement.GetProperty(ResultField).GetProperty(StatusField).GetString();
        if (status != Completed)
        {
            throw new InvalidOperationException(NotCompleted + status);
        }
    }

    public async Task<IReadOnlyList<string>> NearestAsync(float[] query, int limit, CancellationToken token)
    {
        var request = new { query, limit, @params = new { exact = true }, with_payload = PayloadFields, with_vector = false };
        using var reply = await SendAsync(Next, HttpMethod.Post, queryPath, request, token);
        var points = reply.RootElement.GetProperty(ResultField).GetProperty(PointsField);
        var ids = new List<string>(points.GetArrayLength());
        foreach (var point in points.EnumerateArray())
        {
            ids.Add(point.GetProperty(PayloadField).GetProperty(IdField).GetString() ?? throw new KeyNotFoundException(MissingId));
        }
        return ids;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var node in nodes)
        {
            node.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private async Task CreateCollectionAsync(int dimensions, CancellationToken token)
    {
        var copies = nodes.Length;
        var collection = new
        {
            vectors = new { size = dimensions, distance = Cosine },
            shard_number = 1,
            replication_factor = copies,
            write_consistency_factor = (copies / 2) + 1,
            hnsw_config = new { m = 0 },
            optimizers_config = new { indexing_threshold = 0 }
        };
        using var reply = await SendAsync(nodes[0], HttpMethod.Put, CollectionPath, collection, token);
        if (!reply.RootElement.GetProperty(ResultField).GetBoolean())
        {
            throw new InvalidOperationException(NotCreated + reply.RootElement.GetRawText());
        }
    }

    /// <summary>Polls the node every 2 s until its local replica is active; gives up after 5 minutes with the last state.</summary>
    private static async Task WaitActiveAsync(HttpClient node, CancellationToken token)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        var state = await ReadShardStateAsync(node, token);
        while (state != Active)
        {
            if (clock.GetElapsedTime(started) >= ReadinessLimit)
            {
                throw new TimeoutException(NotActive + node.BaseAddress + ": " + state);
            }
            await Task.Delay(RetryInterval, clock, token);
            state = await ReadShardStateAsync(node, token);
        }
    }

    private static async Task<string> ReadShardStateAsync(HttpClient node, CancellationToken token)
    {
        try
        {
            using var reply = await SendAsync(node, HttpMethod.Get, ClusterPath, null, token);
            var shards = reply.RootElement.GetProperty(ResultField).GetProperty(LocalShardsField);
            if (shards.GetArrayLength() == 0)
            {
                return NoLocalShard;
            }
            foreach (var shard in shards.EnumerateArray())
            {
                var state = shard.GetProperty(StateField).GetString() ?? NoState;
                if (state != Active)
                {
                    return state;
                }
            }
            return Active;
        }
        catch (HttpRequestException error) when (!token.IsCancellationRequested)
        {
            return error.Message;
        }
    }

    /// <summary>Qdrant point ids are integers or UUIDs: the dataset id maps to a name-based UUID and stays verbatim in the payload.</summary>
    private static Guid PointId(string id) => new(SHA256.HashData(Encoding.UTF8.GetBytes(id)).AsSpan(0, PointIdBytes));

    private static async Task<JsonDocument> SendAsync(HttpClient client, HttpMethod method, string path, object? body, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, JsonMediaType);
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(RequestFailed + method + " " + path + " returned "
                + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ": "
                + await response.Content.ReadAsStringAsync(token), null, response.StatusCode);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }
}
