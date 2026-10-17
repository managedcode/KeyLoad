namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeProtocol
{
    internal const string CounterSpace = "queue-counters";
    internal const string MetadataSpace = "message-meta";
    internal const string Queue = "deadline-work";
    internal const string Message = "original";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":43}";
    internal const string Headers = "{}";
    internal const string Worker = "deadline-worker";
    internal const string Root = "root";
    internal const long Initial = 0;
    internal const long First = 1;
    internal const long Second = 2;
    internal const long Third = 3;
    internal const int MinimumPageRecords = 2;
    internal const int MaximumDiscoveryPages = 12;
    internal const int RetryCapMilliseconds = 1000;
    internal const string LeaseSpace = "lease";
    internal const string RetryCode = "RetryRequested";
    internal const int DueSeconds = 1;
    internal const int PreviousTick = -1;
}
