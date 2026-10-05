using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorIndex
{
    internal static async Task<VectorIndexReceipt> BuildAsync(HttpClient client, HttpClient[] nodes, string collection,
        VectorComparisonProfile profile, CancellationToken token)
    {
        var path = "/collections/" + collection;
        var watch = Stopwatch.StartNew();
        if (profile.IndexKind == VectorIndexKind.Hnsw)
        {
            foreach (var field in new[] { "filtered", "mixed" })
            {
                using var payload = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, path + "/index?wait=true",
                    new { field_name = field, field_schema = "bool" }, token);
            }
            using var start = await QdrantVectorHttp.SendAsync(client, HttpMethod.Patch, path, new
            {
                hnsw_config = new { m = 16, ef_construct = 200, full_scan_threshold = 0 },
                optimizers_config = new { indexing_threshold = 1 }
            }, token);
        }
        var definition = await AwaitReadyAsync(nodes, path, profile, token);
        watch.Stop();
        return new(profile.IndexKind, definition, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["metric"] = "Cosine", ["m"] = profile.IndexKind == VectorIndexKind.Hnsw ? "16" : "0",
            ["efConstruction"] = "200", ["efSearch"] = "200", ["fullScanThreshold"] = "0",
            ["queryPlannerEvidence"] = "native collection config plus explicit exact/indexed_only query parameters"
        }, profile.IndexKind == VectorIndexKind.Exact ? 0 : watch.Elapsed.TotalMilliseconds);
    }

    private static async Task<string> AwaitReadyAsync(HttpClient[] nodes, string path, VectorComparisonProfile profile,
        CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(90));
        var observations = new JsonElement[nodes.Length];
        while (true)
        {
            var ready = true;
            for (var i = 0; i < nodes.Length; i++)
            {
                using var response = await QdrantVectorHttp.SendAsync(nodes[i], HttpMethod.Get, path, null, deadline.Token);
                var result = response.RootElement.GetProperty("result");
                observations[i] = result.Clone();
                ready &= QdrantVectorIndexValidation.Ready(result, profile);
            }
            if (ready) return JsonSerializer.Serialize(observations);
            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
        }
    }
}
