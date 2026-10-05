using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal static class QdrantVectorIndex
{
    internal static async Task<VectorIndexReceipt> BuildAsync(HttpClient client, HttpClient[] nodes, string collection,
        VectorComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(executionOptions.Value.IndexBuildTimeout);
        token = deadline.Token;
        var path = QdrantVectorProtocol.CollectionPrefix + collection;
        var watch = Stopwatch.StartNew();
        if (profile.IndexKind == VectorIndexKind.Hnsw)
        {
            foreach (var field in new[] { QdrantVectorProtocol.Filtered, QdrantVectorProtocol.Mixed })
            {
                using var payload = await QdrantVectorHttp.SendAsync(client, HttpMethod.Put, path + QdrantVectorProtocol.PayloadIndexPath,
                    new { field_name = field, field_schema = QdrantVectorProtocol.BooleanIndexType }, executionOptions, token);
            }
            using var start = await QdrantVectorHttp.SendAsync(client, HttpMethod.Patch, path, new
            {
                hnsw_config = new { m = QdrantVectorProtocol.HnswConnections, ef_construct = QdrantVectorProtocol.HnswBreadth, full_scan_threshold = QdrantVectorProtocol.DisabledIndex },
                optimizers_config = new { indexing_threshold = QdrantVectorProtocol.MinimumIndexThreshold }
            }, executionOptions, token);
        }
        var definition = await AwaitReadyAsync(nodes, path, profile, executionOptions, token);
        watch.Stop();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [QdrantVectorProtocol.Metric] = QdrantVectorProtocol.CosineMetric,
            [QdrantVectorProtocol.Connections] = profile.IndexKind == VectorIndexKind.Hnsw ? QdrantVectorProtocol.ConnectionsText : QdrantVectorProtocol.DisabledText,
            [QdrantVectorProtocol.QueryPlannerEvidence] = QdrantVectorProtocol.PlannerContract,
            [QdrantVectorProtocol.ExecutionPolicyKey] = JsonSerializer.Serialize(executionOptions.Value)
        };
        if (profile.IndexKind == VectorIndexKind.Hnsw)
        {
            parameters[QdrantVectorProtocol.ConstructionParameter] = QdrantVectorProtocol.BreadthText;
            parameters[QdrantVectorProtocol.SearchParameter] = QdrantVectorProtocol.BreadthText;
            parameters[QdrantVectorProtocol.FullScanParameter] = QdrantVectorProtocol.DisabledText;
        }
        return new(profile.IndexKind, definition, parameters,
            profile.IndexKind == VectorIndexKind.Exact ? QdrantVectorProtocol.EmptyCount : watch.Elapsed.TotalMilliseconds);
    }

    private static async Task<string> AwaitReadyAsync(HttpClient[] nodes, string path, VectorComparisonProfile profile,
        IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(executionOptions.Value.IndexBuildTimeout);
        var observations = new JsonElement[nodes.Length];
        while (true)
        {
            var ready = true;
            for (var i = QdrantVectorProtocol.FirstElementIndex; i < nodes.Length; i++)
            {
                using var response = await QdrantVectorHttp.SendAsync(nodes[i], HttpMethod.Get, path, null, executionOptions, deadline.Token);
                var result = response.RootElement.GetProperty(QdrantVectorProtocol.Result);
                observations[i] = result.Clone();
                ready &= QdrantVectorIndexValidation.Ready(result, profile);
            }
            if (ready)
            {
                return JsonSerializer.Serialize(observations);
            }
            await Task.Delay(executionOptions.Value.IndexPollInterval, deadline.Token);
        }
    }
}
