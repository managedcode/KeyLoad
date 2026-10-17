namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDueDatabaseScopeRf3Protocol
{
    internal const string TenantPrefix = "due-database-scope-";
    internal const string SubjectPrefix = "due-scope-worker-";
    internal const string AllowedDatabase = "b-allowed";
    internal const string DeniedDatabase = "a-denied";
    internal const string Domain = "due-domain";
    internal const string Queue = "same-queue";
    internal const string Message = "same-message";
    internal const string Healthy = "healthy";
    internal const string AllowedPayload = "{\"database\":\"allowed\"}";
    internal const string DeniedPayload = "{\"database\":\"denied\"}";
    internal const string EnqueueKind = "enqueue";
    internal const string AckKind = "Ack";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string PolicyChanged = "The principal policy changed since this command was evaluated.";
    internal const int LeaseSeconds = 30;
}
