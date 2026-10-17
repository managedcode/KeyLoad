namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderRf3Protocol
{
    internal const string SubjectPrefix = "queue-leader-worker-";
    internal const string TenantPrefix = "queue-leader-";
    internal const string Database = "leader-db";
    internal const string Domain = "leader-domain";
    internal const string Queue = "leader-deadline-work";
    internal const string Message = "original-pending";
    internal const string Healthy = "healthy-after-rejoin";
    internal const string Fault = "queue-deadline-original-leader-loss";
    internal const string EnqueueKind = "enqueue";
    internal const string AckKind = "Ack";
    internal const string SafeStaleDetail = "The database operation could not be completed.";
    internal const string DetailKey = "detail";
    internal const string StaleDetail = "The queue deadline condition no longer matches.";
}
