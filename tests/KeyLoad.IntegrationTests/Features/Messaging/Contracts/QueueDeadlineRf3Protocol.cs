namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Protocol
{
    internal const string SubjectPrefix = "due-worker-";
    internal const string TenantPrefix = "due-queue-";
    internal const string Database = "due-db";
    internal const string Domain = "due-work";
    internal const string Queue = "work";
    internal const string Original = "original";
    internal const string Future = "future";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":44}";
    internal const string Headers = "{}";
    internal const int LeadSeconds = 30;
    internal const int FutureHours = 1;
    internal const long Initial = 0;
    internal const long First = 1;
    internal const long Second = 2;
    internal const long Third = 3;
    internal const Capability Worker = Capability.QueueConsume | Capability.QueueInspect | Capability.QueueAck;
}
