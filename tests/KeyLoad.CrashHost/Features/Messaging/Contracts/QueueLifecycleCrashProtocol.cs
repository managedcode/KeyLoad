namespace KeyLoad.CrashHost;

internal static class QueueLifecycleCrashProtocol
{
    internal const string Mode = "queue-lifecycle-crash";
    internal const string OperationFile = "queue-lifecycle-operation.json";
    internal const string Queue = "queue-lifecycle";
    internal const string Parked = "parked";
    internal const string Pending = "pending";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"knowledge\":\"complete lifecycle crash corpus\"}";
    internal const string Headers = "{\"trace\":\"native atomic boundary\"}";
    internal const int None = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int Five = 5;
    internal const int LeaseSeconds = 30;
    internal const int ImageRecords = 4096;
    private const string MessageBodyFamily = "message-body";
    private const string MessageMetadataFamily = "message-meta";
    private const string QueueCountersFamily = "queue-counters";
    private const string ReadyFamily = "ready";
    private const string LeaseFamily = "lease";
    private const string ScheduledFamily = "scheduled";
    private const string DeadLetterFamily = "dead-letter";
    private const string PendingDeadLetterFamily = "queue-pending-dead-letter";
    private const string DeadLetterOrderFamily = "queue-dead-letter-order";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string TransactionDomainId = "messaging";
    private const string PartitionKey = "lifecycle-crash";
    private const string SeedCommandId = "6a5796f5-034b-4ca1-b49d-5ef8636a55b7";
    internal static readonly string[] Families = [MessageBodyFamily, MessageMetadataFamily, QueueCountersFamily, ReadyFamily,
        LeaseFamily, ScheduledFamily, DeadLetterFamily, PendingDeadLetterFamily, DeadLetterOrderFamily];
    internal static readonly PartitionRef Partition = new(TenantId, DatabaseId, TransactionDomainId, PartitionKey);
    internal static readonly Guid CommandId = Guid.Parse(SeedCommandId);
}
