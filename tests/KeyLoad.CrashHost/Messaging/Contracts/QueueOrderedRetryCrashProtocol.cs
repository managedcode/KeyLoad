namespace KeyLoad.CrashHost;

internal static class QueueOrderedRetryCrashProtocol
{
    internal const string Mode = "queue-ordered-retry-crash";
    internal const string OperationFile = "queue-ordered-retry-operation.native";
    internal const string DeliveryFile = "queue-ordered-retry-delivery.native";
    internal const string Queue = "ordered-retry";
    internal const string First = "ordered-a";
    internal const string Second = "ordered-b";
    internal const string Healthy = "ordered-c";
    internal const string Key = "shared-key";
    internal const string Payload = "{\"knowledge\":\"complete ordered retry crash corpus\"}";
    internal const string Headers = "{\"kind\":\"ordered-retry-crash\"}";
    internal const int Initial = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int Five = 5;
    internal const int Six = 6;
    internal const int ImageRecords = 4096;
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Domain = "messaging";
    internal const string PartitionKey = "ordered-retry-crash";
    internal const string OriginalCommandId = "478ab2e0-e7c1-4d96-8b80-856917f18c44";
    internal const string MessageBodyFamily = "message-body";
    internal const string MessageMetadataFamily = "message-meta";
    internal const string QueueCountersFamily = "queue-counters";
    internal const string ReadyFamily = "ready";
    internal const string LeaseFamily = "lease";
    internal const string ScheduledFamily = "scheduled";
    internal const string DeadLetterFamily = "dead-letter";
    internal const string PendingDeadLetterFamily = "queue-pending-dead-letter";
    internal const string DeadLetterOrderFamily = "queue-dead-letter-order";
    internal const string QueueOrderFamily = "queue-order";
    internal const string SeedMismatch = "The native ordered retry seed differs from the full literal.";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal static readonly Guid CommandId = Guid.Parse(OriginalCommandId);
    internal static readonly string[] Families = [MessageBodyFamily, MessageMetadataFamily, QueueCountersFamily, ReadyFamily,
        LeaseFamily, ScheduledFamily, DeadLetterFamily, PendingDeadLetterFamily, DeadLetterOrderFamily, QueueOrderFamily];
}
