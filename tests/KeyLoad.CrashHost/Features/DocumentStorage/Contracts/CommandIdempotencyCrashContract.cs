namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CommandIdempotencyCrashContract
{
    internal const string FirstMode = "document-command-idempotency-first";
    internal const string ReplayMode = "document-command-idempotency-replay";
    internal const string CommandEvidenceFile = "document-command-frozen-input.bin";
    internal const string ReceiptEvidenceFile = "document-command-original-receipt.bin";
    internal const string SeedTailEvidenceFile = "document-command-seed-tail.bin";
    internal const string Collection = "command-restart-documents";
    internal const string StreamSet = "command-restart-events";
    internal const string StreamId = "command-restart-stream";
    internal const string EventId = "command-restart-event";
    internal const string Queue = "command-restart-queue";
    internal const string MessageId = "command-restart-message";
    internal const string DocumentId = "command-restart-document";
    internal const string FollowUpDocumentId = "command-restart-follow-up";
    internal const string DocumentJson = "{\"value\":\"original\"}";
    internal const string ChangedDocumentJson = "{\"value\":\"changed\"}";
    internal const string EventPayloadJson = "{\"kind\":\"restart\"}";
    internal const string QueuePayloadJson = "{\"kind\":\"queued\"}";
    internal const string FollowUpJson = "{\"healthy\":true}";
    internal const string EventType = "RestartVerified";
    internal const string TailKeySpace = "outbox";
    internal const int RetryCount = 100;
    internal const int EvidenceMaximumBytes = 65_536;
    private const string PartitionKey = "command-restart-partition";
    private const string CommandIdText = "e3c5d2eb-20f4-4f82-9ab6-c94a75c2cb8f";
    private const string FollowUpCommandText = "3c83be09-a8a8-4304-8a27-90028e56e4a3";
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant,
        CrashFixtureValues.Database, CrashFixtureValues.Orders, PartitionKey);
    internal static Guid CommandId { get; } = Guid.Parse(CommandIdText);
    internal static Guid FollowUpCommandId { get; } = Guid.Parse(FollowUpCommandText);
}
