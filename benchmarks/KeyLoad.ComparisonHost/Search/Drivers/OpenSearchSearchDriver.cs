using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Search;

/// <summary>
/// OpenSearch over its REST API: one index with a standard-analyzed <c>text</c> field and a keyword <c>id</c>, one primary
/// shard with a replica on every other node and request translog durability, <c>_bulk</c> creates written once every copy is
/// active, <c>_refresh</c> as the index build, and a <c>match</c> query with <c>operator: and</c> ranked by the engine's BM25.
/// Bulk and search calls rotate over the nodes.
/// </summary>
internal sealed class OpenSearchSearchDriver(BenchmarkConnection connection) : ISearchDriver
{
    private const string Index = "benchmark";
    private const string IndexPath = "/" + Index;
    private const string CreatePath = IndexPath + "?wait_for_active_shards=all";
    private const string BulkPath = IndexPath + "/_bulk?wait_for_active_shards=all&refresh=false";
    private const string RefreshPath = IndexPath + "/_refresh";
    private const string SearchPath = IndexPath + "/_search";
    private const string ClusterHealthPath = "/_cluster/health?wait_for_status=green&timeout=30s&wait_for_nodes=";
    private const string IndexHealthPath = "/_cluster/health/" + Index + "?wait_for_status=green&wait_for_active_shards=all&timeout=30s";
    private const string JsonMediaType = "application/json";
    private const string NdjsonMediaType = "application/x-ndjson";
    private const string KeywordType = "keyword";
    private const string TextType = "text";
    private const string StandardAnalyzer = "standard";
    private const string RequestDurability = "request";
    private const string AllTerms = "and";
    private const string Green = "green";
    private const string StatusField = "status";
    private const string TimedOutField = "timed_out";
    private const string AcknowledgedField = "acknowledged";
    private const string ErrorsField = "errors";
    private const string ItemsField = "items";
    private const string CreateField = "create";
    private const string ErrorField = "error";
    private const string ShardsField = "_shards";
    private const string FailedField = "failed";
    private const string HitsField = "hits";
    private const string IdField = "_id";
    private const char LineFeed = '\n';
    private const char Separator = ' ';
    private const string RequestFailed = "OpenSearch ";
    private const string NotGreen = "OpenSearch is not green for ";
    private const string HealthTimedOut = "health wait timed out";
    private const string NoStatus = "no status";
    private const string NotAcknowledged = "OpenSearch did not acknowledge the index: ";
    private const string BulkFailed = "OpenSearch rejected bulk items: ";
    private const string ItemCountMismatch = "the item count differs from the request";
    private const string ShardFailures = "OpenSearch shard failures: ";
    private const string SearchTimedOut = "OpenSearch search timed out.";
    private const string MissingId = "OpenSearch returned a hit without an _id.";
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadinessLimit = TimeSpan.FromMinutes(5);
    private readonly HttpClient[] nodes = [.. Enumerable.Range(0, connection.Endpoints.Count).Select(node => connection.CreateHttpClient(node))];
    private readonly string clusterHealthPath = ClusterHealthPath + connection.Nodes.ToString(CultureInfo.InvariantCulture);
    private readonly int replicas = connection.Nodes - 1;
    private int next = -1;

    public int BatchSize => 100;

    private HttpClient Next => nodes[(int)((uint)Interlocked.Increment(ref next) % (uint)nodes.Length)];

    /// <summary>Waits until every node joined a green cluster, creates the index and waits until all its copies are active.</summary>
    public async Task PrepareAsync(CancellationToken token)
    {
        await WaitForGreenAsync(clusterHealthPath, token);
        await CreateIndexAsync(token);
        await WaitForGreenAsync(IndexHealthPath, token);
    }

    public async Task InsertAsync(IReadOnlyList<SearchDocument> documents, CancellationToken token)
    {
        var body = new StringBuilder();
        foreach (var document in documents)
        {
            body.Append(JsonSerializer.Serialize(new { create = new { _id = document.Id } })).Append(LineFeed)
                .Append(JsonSerializer.Serialize(new { id = document.Id, text = document.Text })).Append(LineFeed);
        }
        using var reply = await SendAsync(Next, HttpMethod.Post, BulkPath, body.ToString(), NdjsonMediaType, token);
        var root = reply.RootElement;
        if (root.GetProperty(ErrorsField).GetBoolean() || root.GetProperty(ItemsField).GetArrayLength() != documents.Count)
        {
            throw new InvalidOperationException(BulkFailed + FirstError(root));
        }
    }

    /// <summary>Refreshes every copy of the green index so that all acknowledged documents are searchable.</summary>
    public async Task BuildIndexAsync(CancellationToken token)
    {
        await WaitForGreenAsync(IndexHealthPath, token);
        using var refreshed = await SendAsync(nodes[0], HttpMethod.Post, RefreshPath, null, JsonMediaType, token);
        RequireShards(refreshed.RootElement);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(IReadOnlyList<string> terms, int limit, CancellationToken token)
    {
        var match = new { text = new { query = string.Join(Separator, terms), @operator = AllTerms } };
        var request = new { size = limit, _source = false, query = new { match } };
        using var reply = await SendAsync(Next, HttpMethod.Post, SearchPath, JsonSerializer.Serialize(request), JsonMediaType, token);
        var root = reply.RootElement;
        if (root.GetProperty(TimedOutField).GetBoolean())
        {
            throw new TimeoutException(SearchTimedOut);
        }
        RequireShards(root);
        var hits = root.GetProperty(HitsField).GetProperty(HitsField);
        var ids = new List<string>(hits.GetArrayLength());
        foreach (var hit in hits.EnumerateArray())
        {
            ids.Add(hit.GetProperty(IdField).GetString() ?? throw new KeyNotFoundException(MissingId));
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

    private async Task CreateIndexAsync(CancellationToken token)
    {
        var index = new
        {
            settings = new { number_of_shards = 1, number_of_replicas = replicas, translog = new { durability = RequestDurability } },
            mappings = new { properties = new { id = new { type = KeywordType }, text = new { type = TextType, analyzer = StandardAnalyzer } } }
        };
        using var created = await SendAsync(nodes[0], HttpMethod.Put, CreatePath, JsonSerializer.Serialize(index), JsonMediaType, token);
        if (!created.RootElement.GetProperty(AcknowledgedField).GetBoolean())
        {
            throw new InvalidOperationException(NotAcknowledged + created.RootElement.GetRawText());
        }
    }

    /// <summary>Polls the health API every 2 s until it reports green; gives up after 5 minutes with the last state.</summary>
    private async Task WaitForGreenAsync(string path, CancellationToken token)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        var state = await ReadHealthAsync(path, token);
        while (state != Green)
        {
            if (clock.GetElapsedTime(started) >= ReadinessLimit)
            {
                throw new TimeoutException(NotGreen + path + ": " + state);
            }
            await Task.Delay(RetryInterval, clock, token);
            state = await ReadHealthAsync(path, token);
        }
    }

    private async Task<string> ReadHealthAsync(string path, CancellationToken token)
    {
        try
        {
            using var health = await SendAsync(nodes[0], HttpMethod.Get, path, null, JsonMediaType, token);
            var root = health.RootElement;
            return root.GetProperty(TimedOutField).GetBoolean() ? HealthTimedOut : root.GetProperty(StatusField).GetString() ?? NoStatus;
        }
        catch (HttpRequestException error) when (!token.IsCancellationRequested)
        {
            return error.Message;
        }
    }

    private static void RequireShards(JsonElement root)
    {
        var shards = root.GetProperty(ShardsField);
        if (shards.GetProperty(FailedField).GetInt32() != 0)
        {
            throw new InvalidOperationException(ShardFailures + shards.GetRawText());
        }
    }

    private static string FirstError(JsonElement root)
    {
        foreach (var item in root.GetProperty(ItemsField).EnumerateArray())
        {
            if (item.GetProperty(CreateField).TryGetProperty(ErrorField, out var error))
            {
                return error.GetRawText();
            }
        }
        return ItemCountMismatch;
    }

    private static async Task<JsonDocument> SendAsync(HttpClient client, HttpMethod method, string path, string? body, string mediaType,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, mediaType);
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
