namespace KeyLoad.RecoveryTests;

internal static class QueueClaimAckCrashProtocol
{
    internal const string ClaimMode = "queue-claim-commit-crash";
    internal const string AckMode = "queue-ack-commit-crash";
    internal const string OperationFile = "queue-claim-ack-operation.execution";
    internal const string BaselineFile = "queue-claim-ack-baseline.execution";
    internal const string TargetFile = "queue-claim-ack-target.execution";
    internal const string PhysicalTargetFile = "queue-claim-ack-physical-target.execution";
    internal const int AdjacentPosition = 1;
    internal const int PlacementVersion = 1;
    internal const string DeliveryFile = "queue-claim-ack-delivery.execution";
    internal const string Queue = "claim-ack";
    internal const string Message = "original";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":1}";
    internal const string Headers = "{\"kind\":\"claim-ack\"}";
    internal const string TenantId = "tenant";
    internal const string DatabaseId = "database";
    internal const string TransactionDomain = "messaging";
    internal const string PartitionKey = "claim-ack-crash";
    internal const string OperationIdText = "28934c93-979f-4af4-9c50-2f1a69dd9f25";
    internal const string BaselineBoundExceeded = "The original claim/ACK baseline exceeded its admitted bound.";
    internal const string ImageRowSeparator = ":";
    internal const int MaximumClaimMessages = 1;
    internal const int LeaseSeconds = 30;
    internal const int ImageRecords = 4096;
    internal static readonly PartitionRef Partition = new(TenantId, DatabaseId, TransactionDomain, PartitionKey);
    internal static readonly QueueLaneRef Lane = new(Partition, Queue);
    internal static readonly Guid OperationId = Guid.Parse(OperationIdText);
}
