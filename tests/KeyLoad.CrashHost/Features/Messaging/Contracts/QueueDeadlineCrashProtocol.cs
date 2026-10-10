namespace KeyLoad.CrashHost;

internal static class QueueDeadlineCrashProtocol
{
    internal const string Mode = "queue-deadline-crash";
    internal const string OperationFile = "queue-deadline-operation.json";
    internal const string Queue = "deadline-work";
    internal const string Message = "original";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":45}";
    internal const string Headers = "{}";
    internal const string ReceiptKind = "advanceQueueDeadline";
    internal const string CounterSpace = "queue-counters";
    internal const string BodySpace = "message-body";
    internal const string MetadataSpace = "message-meta";
    internal const int BusinessLifetimeSeconds = 30;
    internal const long Initial = 0;
    internal const long First = 1;
    internal const long Second = 2;
    internal const long Third = 3;
    private const string Tenant = "deadline-tenant";
    private const string Database = "deadline-db";
    private const string Domain = "deadline-work";
    private const string PartitionKey = "original";
    internal static PartitionRef Partition { get; } = new(Tenant, Database, Domain, PartitionKey);
}
