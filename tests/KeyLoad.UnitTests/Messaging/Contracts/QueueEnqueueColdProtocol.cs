namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueEnqueueColdProtocol
{
    internal const string Queue = "jobs";
    internal const string Collection = "orders";
    internal const string Root = "root";
    internal const string Publisher = "publisher";
    internal const string Ready = "ready-original";
    internal const string Scheduled = "scheduled-original";
    internal const string Healthy = "healthy";
    internal const string Document = "producer";
    internal const string RefusedDocument = "refused-producer";
    internal const string ReadyJson = "{\"work\":1}";
    internal const string ScheduledJson = "{\"work\":2}";
    internal const string HealthyJson = "{\"work\":3}";
    internal const string Headers = "{\"kind\":\"job\"}";
    internal const string EmptyHeaders = "{}";
    internal const string PutKind = "putDocument";
    internal const string EnqueueKind = "enqueue";
    internal const long InitialPolicyEpoch = 1;
    internal const long InitialRevision = 1;
    internal const string BodyFamily = "message-body";
    internal const string MetadataFamily = "message-meta";
    internal const string CountersFamily = "queue-counters";
    internal const string ReadyFamily = "ready";
    internal const string ScheduledFamily = "scheduled";
    internal const int MaximumStoredMessages = 2;
    internal const int FutureScheduledHours = 1;
    internal const long RestoredEpoch = 2;
    internal const long FirstReadySequence = 1;
    internal const long HealthyReadySequence = 2;
    internal const int InitialAttempts = 0;
    internal const long InitialStateVersion = 1;
    internal const long ScheduledReadySequence = 0;
    internal const long NoInFlight = 0;
}
