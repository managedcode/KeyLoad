using System.Text.Json;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class ModelSourceArgumentClusterRequests
{
    private const string Collate = " COLLATE Latin1_General_CI_AS";
    private const string EventPrefix = "SELECT TOP (3) e.eventType FROM EVENTS(";
    private const string EventSuffix = ") e ORDER BY e.revision";
    private const string QueuePrefix = "SELECT TOP (3) q.id, q.state FROM QUEUE_MESSAGES(";
    private const string QueueSuffix = ") q ORDER BY q.id";

    internal static (QueryRequest Refused, QueryRequest Healthy, bool Event)[] All(PartitionRef partition)
        =>
        [
            Pair(partition, "'model-events'" + Collate + ", 'order-history', 1", "'model-events', 'order-history', 1", true),
            Pair(partition, "'model-events', 'order-history'" + Collate + ", 1", "'model-events', 'order-history', 1", true),
            Pair(partition, "@source" + Collate + ", 'order-history', 1", "@source, 'order-history', 1", true, "source", "model-events"),
            Pair(partition, "'model-events', 'order-history', @generation" + Collate, "'model-events', 'order-history', @generation", true, "generation", 1L),
            Pair(partition, "'model-jobs'" + Collate, "'model-jobs'", false),
            Pair(partition, "@source" + Collate, "@source", false, "source", "model-jobs")
        ];

    private static (QueryRequest Refused, QueryRequest Healthy, bool Event) Pair(PartitionRef partition,
        string refused, string healthy, bool events, string? name = null, object? value = null)
    {
        var prefix = events ? EventPrefix : QueuePrefix;
        var suffix = events ? EventSuffix : QueueSuffix;
        Dictionary<string, JsonElement>? parameters = name is null ? null : new(StringComparer.Ordinal)
        {
            [name] = JsonSerializer.SerializeToElement(value, JsonDefaults.Options)
        };
        return (new(partition, prefix + refused + suffix, parameters, AllowFullScan: true),
            new(partition, prefix + healthy + suffix, parameters, AllowFullScan: true), events);
    }
}
