namespace KeyLoad.CrashHost;

internal static class TopicPurgeCrashContract
{
    internal const string Mode = "topic-purge-native-crash";
    internal const string Topic = "purge-topic";
    internal const string Documents = "purge-documents";
    internal const string Queue = "purge-work";
    internal const string Group = "paused";
    internal const string OperationFile = "purge-operation.bin";
    internal const string SeedOperationFile = "purge-seed-operation.bin";
    internal const string SeedReceiptFile = "purge-seed-receipt.bin";
    internal const string PublishOperationFile = "purge-publish-operation.bin";
    internal const string PublishReceiptFile = "purge-publish-receipt.bin";
    internal const string PositionFile = "purge-position.bin";
    internal const string ProtectedFile = "purge-protected.bin";
    internal const string PinnedFile = "purge-pinned.bin";
    internal const string CursorFile = "purge-cursor.bin";
    internal const string DocumentJson = "{\"retained\":true}";
    internal const string QueueJson = "{\"work\":true}";
    internal const string HeadersJson = "{\"kind\":\"purge\"}";
    internal const int FixtureRecordLimit = 4096;
    internal const string PinnedSuffix = "-pinned";
    private const string SeedIdentity = "6524df62-51c8-4a97-919e-e2c7b5d00101";
    private const string PurgeIdentity = "6524df62-51c8-4a97-919e-e2c7b5d00102";
    private const string PublishIdentity = "6524df62-51c8-4a97-919e-e2c7b5d00103";
    private const string HealthyIdentity = "6524df62-51c8-4a97-919e-e2c7b5d00104";
    private const string EventPrefix = "event";
    private const string EventType = "Created";
    private const string EventJsonPrefix = "{\"n\":";
    private const string EventJsonSuffix = "}";
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant,
        CrashFixtureValues.Database, CrashFixtureValues.Orders, Mode);
    internal static EventSourceRef Source { get; } = new(Partition, Topic, EventSourceKind.Topic);
    internal static SubscriptionRef Subscription { get; } = new(Source, Group);
    internal static QueueLaneRef Lane { get; } = new(Partition, Queue);
    internal static Guid SeedId { get; } = Guid.Parse(SeedIdentity);
    internal static Guid PurgeId { get; } = Guid.Parse(PurgeIdentity);
    internal static Guid PublishId { get; } = Guid.Parse(PublishIdentity);
    internal static Guid HealthyId { get; } = Guid.Parse(HealthyIdentity);
    internal static EventData Event(int position) => new(EventPrefix + position, EventType, EventJsonPrefix + position + EventJsonSuffix);
    internal static CommandRequest Purge(Guid id, long cut) => new(id, Partition, [new PurgeTopic(Topic, cut)]);
}
