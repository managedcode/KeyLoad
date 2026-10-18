
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyClusterProtocol
{
    internal const string Tenant = "terminal-policy-";
    internal const string Database = "terminal-policy-db";
    internal const string Domain = "terminal-policy-domain";
    internal const string Queue = "terminal-policy-jobs";
    internal const string Collection = "terminal-policy-documents";
    internal const string Message = "original-retry";
    internal const string Healthy = "healthy-after-terminal";
    internal const string Refused = "refused-producer";
    internal const string Payload = "{\"kind\":\"retry\"}";
    internal const string Headers = "{}";
    internal const string Retry = "RetryRequested";
    internal const string Exhausted = "AttemptsExhausted";
    internal const string Enqueue = "enqueue";
    internal const string Put = "putDocument";
    internal const int Attempts = 3;
    internal const int LeaseSeconds = 30;
    internal const string Cold = "queue-terminal-policy-public-cold";
}
