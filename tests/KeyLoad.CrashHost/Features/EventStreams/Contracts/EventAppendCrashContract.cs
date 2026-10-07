namespace KeyLoad.CrashHost;

internal static class EventAppendCrashContract
{
    internal const string Mode = "event-append-seeded-crash";
    internal const string OperationFile = "event-append-input.bin";
    internal const string SeedPositionFile = "event-append-seed-position.bin";
    internal const string SeedTailFile = "event-append-seed-tail.bin";
    internal const string Documents = "append-documents";
    internal const string Streams = "append-events";
    internal const string Queue = "append-jobs";
    internal const string StreamId = "seeded-stream";
    internal const string SeedId = "seed-event";
    internal const string EventId = "producer-event";
    internal const string Producer = "producer";
    internal const string Healthy = "healthy";
    internal const string EventType = "Created";
    internal const string DocumentJson = "{\"producer\":true}";
    internal const string ChangedJson = "{\"producer\":false}";
    internal const string PayloadJson = "{\"work\":true}";
    internal const string HeadersJson = "{\"kind\":\"append\"}";
    internal const string EventJson = "{\"event\":true}";
    internal const string Principal = "root";
    internal const long SeedStreamRevision = 1;
    private const long MissingDocumentRevision = 0;
    private const string CommandText = "36f96d2d-4dd9-4653-a6c1-40e958047a46";
    private const string SeedText = "42476b2e-f933-47c1-ad4c-f527c0490c34";
    private const string HealthyText = "be3b80d0-cff3-4a25-b89b-d7838e259e45";
    internal static Guid CommandId { get; } = Guid.Parse(CommandText);
    internal static Guid SeedCommandId { get; } = Guid.Parse(SeedText);
    internal static Guid HealthyCommandId { get; } = Guid.Parse(HealthyText);
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant,
        CrashFixtureValues.Database, CrashFixtureValues.Orders, Mode);
    internal static StreamRef Stream { get; } = new(Partition, Streams, StreamId);
    internal static QueueLaneRef Lane { get; } = new(Partition, Queue);
    internal static EventData Event(string id) => new(id, EventType, EventJson);
    internal static CommandRequest ProducerCommand(Guid id, string producer, long expectedRevision)
        => new(id, Partition,
            [new PutDocument(Documents, producer, DocumentJson, MissingDocumentRevision),
                new AppendEvents(Streams, StreamId, [Event(producer == Producer ? EventId : Healthy)],
                    ExpectedStreamRevision.Exact(expectedRevision)),
                new EnqueueMessage(Queue, producer, PayloadJson, HeadersJson)]);
}
