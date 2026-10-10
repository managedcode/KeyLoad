using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLeaseRf3Protocol
{
    internal const string TenantPrefix = "lease-fencing-rf3-";
    internal const string Database = "lease-db";
    internal const string Domain = "lease-work";
    internal const string Queue = "lease-jobs";
    internal const string FutureQueue = "lease-future";
    internal const string FutureMessage = "future";
    internal const int FutureScheduledHours = 1;
    internal const string Message = "original";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":1}";
    internal const string Headers = "{\"kind\":\"lease\"}";
    internal const string HealthyPayload = "{\"work\":2}";
    internal const string ColdScenario = "queue-renew-reclaim-cold";
    internal const string MissingSetup = "The actual queue lease predecessor was not established.";
    internal const int InitialClaim = 1;
    internal const int NextClaim = 1;
    internal const Capability WorkerCapabilities = Capability.QueueConsume | Capability.QueueAck
        | Capability.QueueRenew | Capability.QueueInspect;
    internal static IReadOnlyList<string> Nodes { get; } = Array.AsReadOnly(new[]
        { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 });
}
