namespace KeyLoad.CrashHost;

internal static class QueueEnqueueCrashProtocol
{
    internal const string Mode = "queue-enqueue-commit-crash";
    internal const string OperationFile = "queue-enqueue-operation.native";
    internal const string BaselineFile = "queue-enqueue-baseline.native";
    internal const string TargetFile = "queue-enqueue-target.native";
    internal const string Queue = "enqueue";
    internal const string Collection = "producer";
    internal const string Ready = "ready";
    internal const string Scheduled = "scheduled";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":1}";
    internal const string HealthyPayload = "{\"work\":2}";
    internal const string Headers = "{\"kind\":\"enqueue\"}";
    internal const string BodyFamily = "message-body";
    internal const string MetadataFamily = "message-meta";
    internal const string CountersFamily = "queue-counters";
    internal const string ReadyFamily = "ready";
    internal const string ScheduledFamily = "scheduled";
    internal const string LeaseFamily = "lease";
    internal const string PutKind = "putDocument";
    internal const string EnqueueKind = "enqueue";
    internal const string ImageSeparator = ":";
    internal const string DirectoryPrefix = "keyload-enqueue-crash-";
    internal const string BaselineExceeded = "The original enqueue baseline exceeded its admitted bound.";
    internal const string TerminalDomainExceeded = "The terminal enqueue domain exceeded its admitted bound.";
    internal const string UnexpectedMutation = "Unexpected enqueue producer mutation.";
    // Independent public contract oracle reviewed against native private DuplicateMessageId.
    internal const string ExpectedDuplicateMessageDetail = "A message ID is already present in this lane.";
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Domain = "messaging";
    internal const string PartitionKey = "enqueue-crash";
    internal const string OperationIdText = "8437c2fe-edc3-4940-8e82-93cb1f0b05f8";
    internal const int Initial = 0;
    internal const int First = 1;
    internal const int Second = 2;
    internal const int Settled = 3;
    internal const long FirstLong = 1;
    internal const long SecondLong = 2;
    internal const int LeaseSeconds = 30;
    internal const int ImageRecords = 4096;
    internal const int AdjacentPosition = 1;
    internal const int PlacementVersion = 1;
    internal const int FutureHours = 1;
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal static readonly QueueLaneRef Lane = new(Partition, Queue);
    internal static readonly Guid OperationId = Guid.Parse(OperationIdText);
}
