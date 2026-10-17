using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ModelSourceArgumentRequests
{
    private const string Collate = " COLLATE Latin1_General_CI_AS";
    private const string EventPrefix = "SELECT TOP (4) e.eventType FROM EVENTS(";
    private const string EventSuffix = ") e WHERE e.payload.visible = 'event-visible' ORDER BY e.revision DESC";
    private const string QueuePrefix = "SELECT TOP (4) q.id, q.state FROM QUEUE_MESSAGES(";
    private const string QueueSuffix = ") q ORDER BY q.id DESC";

    internal static (QueryRequest Refused, QueryRequest Healthy, bool Event)[] All(PartitionRef partition)
        =>
        [
            Pair(partition, "'events'" + Collate + ", 'stream-a', 1", "'events', 'stream-a', 1", true),
            Pair(partition, "'events', 'stream-a'" + Collate + ", 1", "'events', 'stream-a', 1", true),
            Pair(partition, "@source" + Collate + ", 'stream-a', 1", "@source, 'stream-a', 1", true, "source", "events"),
            Pair(partition, "'events', 'stream-a', @generation" + Collate, "'events', 'stream-a', @generation", true, "generation", 1L),
            Pair(partition, "'jobs'" + Collate, "'jobs'", false),
            Pair(partition, "@source" + Collate, "@source", false, "source", "jobs")
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
