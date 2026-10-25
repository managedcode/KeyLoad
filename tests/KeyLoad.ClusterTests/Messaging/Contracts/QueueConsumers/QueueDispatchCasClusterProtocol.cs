namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDispatchCasClusterProtocol
{
    internal const string TenantPrefix = "queue-dispatch-cas-";
    internal const string SubjectPrefix = "queue-dispatch-member-";
    internal const string Database = "dispatch-database";
    internal const string Domain = "dispatch-domain";
    internal const string Queue = "dispatch-cas-jobs";
    internal const string Message = "dispatch-message";
    internal const string Payload = "{\"value\":\"dispatch-ready\"}";
    internal const string Headers = "{}";
    internal const string PrivateField = "/secret";
    internal const string PrivateHeader = "/token";
    internal const string Classification = "dispatch-private";
    internal const string EnqueueKind = "enqueue";
    internal const int OneMessage = 1;
    internal const int Attempts = 4;
    internal const long InitialVersion = 1;
    internal const long VersionStep = 1;
}
