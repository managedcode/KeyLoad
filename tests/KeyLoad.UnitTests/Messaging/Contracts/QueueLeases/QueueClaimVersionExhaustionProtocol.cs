namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueClaimVersionExhaustionProtocol
{
    internal const string Queue = "jobs";
    internal const string Root = "root";
    internal const string Message = "input";
    internal const string Payload = "{\"input\":1}";
    internal const string Headers = "{\"kind\":\"job\"}";
    internal const string Detail = "The queue claim counters are exhausted.";
    internal const string MetadataSpace = "message-meta";
    internal const string BodySpace = "message-body";
    internal const string CounterSpace = "queue-counters";
    internal const string ReadySpace = "ready";
    internal const string LeaseSpace = "lease";
    internal const string ScheduledSpace = "scheduled";
    internal const int ImageRecords = 4096;
    internal const int LeaseSeconds = 30;
    internal static readonly string[] Families = [MetadataSpace, BodySpace, CounterSpace, ReadySpace, LeaseSpace, ScheduledSpace];
}
