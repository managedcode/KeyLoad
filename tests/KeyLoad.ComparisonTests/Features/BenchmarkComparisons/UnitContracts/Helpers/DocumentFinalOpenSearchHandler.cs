using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Controlled original HTTP responses exercise final native wire proof; these are not benchmark measurements.</summary>
internal sealed class DocumentFinalOpenSearchHandler(DocumentComparisonSchedule schedule, string corruption) : HttpMessageHandler
{
    internal const string Index = "controlled-documents";
    internal readonly HashSet<string> ReadCopies = new(StringComparer.Ordinal);
    internal int FlushRequests { get; private set; }
    private int states;

    private const string FirstShardField = "0", ShardsSetting = "index.number_of_shards", ReplicasSetting = "index.number_of_replicas";
    private const string DurabilitySetting = "index.translog.durability", KnnSetting = "index.knn";
    private const string SearchAfterField = "search_after", NumberField = "number";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        if (uri.AbsolutePath.Contains("/_cluster/health/", StringComparison.Ordinal))
        {
            return Response(new { timed_out = false, status = "green", number_of_nodes = 3, number_of_data_nodes = 3, active_primary_shards = 1, active_shards = 3, unassigned_shards = 0 });
        }

        if (uri.AbsolutePath.Contains("/_cluster/state/", StringComparison.Ordinal))
        {
            return Response(State());
        }

        if (uri.AbsolutePath == "/_nodes")
        {
            return Response(Nodes());
        }

        if (uri.AbsolutePath.EndsWith("/_flush", StringComparison.Ordinal))
        {
            FlushRequests++;
            return Response(new { _shards = new { total = 3, successful = corruption == "flush" ? 2 : 3, failed = corruption == "flush" ? 1 : 0 } });
        }
        if (uri.AbsolutePath.EndsWith("/_refresh", StringComparison.Ordinal))
        {
            return Response(new { _shards = new { total = 3, successful = 3, failed = 0 } });
        }

        if (uri.AbsolutePath.EndsWith("/_settings", StringComparison.Ordinal))
        {
            return Response(Settings());
        }

        if (uri.AbsolutePath.EndsWith("/_search", StringComparison.Ordinal))
        {
            return Response(await SearchAsync(request, token).ConfigureAwait(false));
        }

        throw new InvalidOperationException("Unexpected final copy proof request.");
    }
    private object State()
    {
        states++;
        var nodes = new[] { "n1", "n2", corruption == "membership" && states > 1 ? "n4" : "n3" };
        var shards = new Dictionary<string, object>
        {
            [FirstShardField] = nodes.Select((node, index) => new { node, primary = index == 0, state = "STARTED" }).ToArray()
        };
        var indices = new Dictionary<string, object> { [Index] = new { shards } };
        return new { cluster_manager_node = "n1", nodes = nodes.ToDictionary(node => node, _ => new { }), routing_table = new { indices } };
    }
    private static object Nodes()
        => new
        {
            _nodes = new { total = 3, successful = 3, failed = 0 },
            nodes = new[] { "n1", "n2", "n3" }.ToDictionary(node => node,
                _ => new { version = OpenSearchNames.ExpectedVersion, roles = new[] { "data", "cluster_manager" } })
        };
    private static Dictionary<string, object> Settings()
    {
        var settings = new Dictionary<string, object>
        {
            [ShardsSetting] = "1",
            [ReplicasSetting] = "2",
            [DurabilitySetting] = "request",
            [KnnSetting] = "false"
        };
        return new Dictionary<string, object> { [Index] = new { settings } };
    }
    private async Task<object> SearchAsync(HttpRequestMessage request, CancellationToken token)
    {
        var query = Uri.UnescapeDataString(request.RequestUri!.Query);
        var node = new[] { "n1", "n2", "n3" }.Single(value => query.Contains("_only_nodes:" + value, StringComparison.Ordinal));
        ReadCopies.Add(node);
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token).ConfigureAwait(false));
        var documents = body.RootElement.TryGetProperty(SearchAfterField, out _) ? []
            : schedule.FinalDocuments().Select(document => SearchDocument(document, node)).ToArray();
        return new
        {
            timed_out = false,
            _shards = new { total = 1, successful = 1, failed = 0 },
            hits = new { total = new { value = schedule.Final, relation = "eq" }, hits = documents }
        };
    }
    private object SearchDocument(BenchmarkDocument document, string node)
    {
        var json = document.Json;
        if (corruption == "content" && node == "n2")
        {
            var parsed = JsonNode.Parse(json)!;
            parsed[NumberField] = -1;
            json = parsed.ToJsonString();
        }
        return new { _id = document.Id, _source = OpenSearchDocument.CreateWithoutVector(document.Id, json), sort = new[] { document.Id } };
    }
    private static HttpResponseMessage Response(object value)
        => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
