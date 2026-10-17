using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadLetterRf3Protocol
{
    internal const string TenantPrefix = "retry-dlq-rf3-";
    internal const string Database = "retry-database";
    internal const string Domain = "retry-domain";
    internal const string Queue = "terminal-jobs";
    internal const string HealthyQueue = "healthy-jobs";
    internal const string Collection = "producer-effects";
    internal const string Message = "pending";
    internal const string Refused = "refused";
    internal const string Payload = "{\"work\":1,\"text\":\"повтор\"}";
    internal const string Headers = "{\"kind\":\"retained\"}";
    internal const string OrderingKey = "reference-only-key";
    internal const string Exhausted = "AttemptsExhausted";
    internal const string ColdScenario = "retry-dlq-cold-owner";
    internal const int One = 1;
    internal const long First = 1;
    internal const long Second = 2;
    internal const string Root = "root";
    internal const string EnqueueKind = "enqueue";
    internal const long ClaimVersion = 2;
    internal const long TerminalVersion = 3;
    internal const int LeaseSeconds = 30;
    internal static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
}
