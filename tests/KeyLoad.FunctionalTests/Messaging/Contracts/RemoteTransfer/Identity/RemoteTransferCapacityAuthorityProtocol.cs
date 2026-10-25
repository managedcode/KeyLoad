namespace KeyLoad.FunctionalTests.Messaging;

internal static class RemoteTransferCapacityAuthorityProtocol
{
    internal const string Payload = "{\"capacityAuthority\":94}";
    internal const string Headers = "{\"origin\":\"retained-execution\"}";
    internal const string Original = "capacity-original";
    internal const string Second = "capacity-second";
    internal const string Pending = "capacity-pending";
    internal const string Fresh = "capacity-fresh";
    internal const string Healthy = "capacity-healthy";
    internal const string Documents = "capacity-documents";
    internal const string FreshDocument = "capacity-protected-document";
    internal const string Corrupt = "Persisted queue transfer capacity is inconsistent.";
    internal const string RuntimeMissingRow = "The genuine capacity-authority seed row is missing.";
    internal const string RuntimeImageExceeded = "The complete transfer-capacity image exceeded its original fixture bound.";
    internal const int First = 1;
    internal const int SecondValue = 2;
    internal const int Third = 3;
    internal const int NoUsage = 0;
    internal const int ImageBound = 512;
    internal const int LeaseSeconds = 30;
    internal const int PolicyEpoch = 1;
    internal const string Body = "message-body";
    internal const string Metadata = "message-meta";
    internal const string Counters = "queue-counters";
    internal const string Ready = "ready";
    internal const string Lease = "lease";
    internal const string Scheduled = "scheduled";
    internal const string PendingDeadLetter = "queue-pending-dead-letter";
    internal const string Parked = "queue-dead-letter-order";
    internal const string DeadLetter = "dead-letter";
    internal const string QueueOrder = "queue-order";
}
