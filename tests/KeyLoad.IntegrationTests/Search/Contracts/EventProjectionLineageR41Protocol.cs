namespace KeyLoad.IntegrationTests.Search;

internal static class EventProjectionLineageR41Protocol
{
    internal const string Collection = "event-lineage-r41";
    internal const string Events = "event-lineage-events-r41";
    internal const string Source = "source";
    internal const string Target = "derived";
    internal const string Baseline = "baseline";
    internal const string Stream = "source-stream";
    internal const string Event = "source-event";
    internal const string EventType = "SourceDocumentUpdated";
    internal const string Input = "/input";
    internal const string Vector = "/embedding";
    internal const string Class = "event-lineage-private-r41";
    internal const string InputRead = "lineage.input.read";
    internal const string InputUse = "lineage.input.use";
    internal const string InputWrite = "lineage.input.write";
    internal const string VectorUse = "lineage.vector.use";
    internal const string VectorWrite = "lineage.vector.write";
    internal const string SourceJson = "{\"input\":\"projection-r41-private-canary\"}";
    internal const string TargetJson = "{\"kind\":\"derived\"}";
    internal const string BaselineJson = "{\"kind\":\"baseline\"}";
    internal const string EventJson = "{\"sourceRevision\":1}";
    internal const string Put = "putDocument";
    internal const string PutVector = "putVector";
    internal const string Delete = "deleteDocument";
    internal const string Append = "appendEvents";
    internal const string Projection = "applyVectorProjection";
    internal const string Unavailable = "The graph vertex is unavailable.";
    internal const string McpUnavailable = "The database operation could not be completed.";
    internal static VectorSpace Space { get; } = new("event-lineage-space-r41", 2, DistanceMetric.Cosine, "event-lineage-model", "v1");
}
