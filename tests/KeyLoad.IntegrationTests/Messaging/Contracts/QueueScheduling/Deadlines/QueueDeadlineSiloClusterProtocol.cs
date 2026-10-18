namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineSiloClusterProtocol
{
    internal const string SubjectPrefix = "queue-silo-worker-";
    internal const string TenantPrefix = "queue-silo-";
    internal const string Database = "silo-db";
    internal const string Domain = "silo-domain";
    internal const string Queue = "silo-deadline-work";
    internal const string Message = "original-pending";
    internal const string Healthy = "healthy-after-rejoin";
    internal const string Fault = "queue-deadline-original-silo-restart";
    internal const string EnqueueKind = "enqueue";
    internal const string AckKind = "Ack";
    internal const string SafeStaleDetail = "The database operation could not be completed.";
    internal const string DetailKey = "detail";
    internal const string StaleDetail = "The queue deadline condition no longer matches.";
}
