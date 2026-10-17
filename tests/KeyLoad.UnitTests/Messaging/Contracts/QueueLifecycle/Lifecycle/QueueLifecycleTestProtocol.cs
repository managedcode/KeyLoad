namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleTestProtocol
{
    internal const string Queue = "lifecycle";
    internal const string Collection = "lifecycle-documents";
    internal const string DeniedAdministrator = "lifecycle-denied-operator";
    internal const string Administrator = "lifecycle-operator";
    internal const string Parked = "a-parked";
    internal const string Pending = "b-active";
    internal const string Held = "c-held";
    internal const string Healthy = "d-healthy";
    internal const string Payload = "{\"knowledge\":\"native same-root lifecycle\"}";
    internal const string Headers = "{\"trace\":\"complete independent headers\"}";
    internal const string OrderingKey = "entity-1";
    internal const string ReadySpace = "ready";
    internal const string ScheduledSpace = "scheduled";
    internal const string LeaseSpace = "lease";
    internal const string BodySpace = "message-body";
    internal const string MetadataSpace = "message-meta";
    internal const string CounterSpace = "queue-counters";
    internal const string DeadLetterSpace = "dead-letter";
    internal const string PendingSpace = "queue-pending-dead-letter";
    internal const string ParkedSpace = "queue-dead-letter-order";
    internal const int None = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int Five = 5;
    internal const int Six = 6;
    internal const int Seven = 7;
    internal const int LeaseSeconds = 30;
    internal const int ImageRecords = 4096;
    internal const string BodyFixtureMismatch = "The lifecycle fixture bodies do not fit the same original byte sublimit.";
}
