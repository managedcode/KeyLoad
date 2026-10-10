using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Protocol
{
    internal const string TenantPrefix = "producer-rf3-";
    internal const string Database = "producer-db";
    internal const string Domain = "producer-work";
    internal const string Queue = "jobs";
    internal const string Collection = "producers";
    internal const string Original = "original";
    internal const string Scheduled = "scheduled";
    internal const string Refused = "refused";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":1}";
    internal const string HealthyPayload = "{\"work\":2}";
    internal const string Headers = "{\"producer\":true}";
    internal const string CommandHeader = "X-KeyLoad-Command-Id";
    internal const string CommandPath = "/v1/commands";
    internal const string ColdScenario = "producer-batch-cold";
    internal const string MissingState = "The actual producer state was not observed.";
    internal const int FutureHours = 1;
    internal const int NoFailures = 0;
    internal const long PolicyEpochAdvance = 1;
    internal const long StateVersionAdvance = 1;
    internal const long StoredMessageCapacity = 2;
    internal const long InitialRevision = 1;
    internal const int OriginalMutationCount = 3;
    internal const int InitialAttempts = 0;
    internal const long UnclaimedLeaseVersion = 0;
    internal const long InitialGeneration = 1;
    internal const long InitialReadySequence = 1;
    internal const long ScheduledReadySequence = 0;
    internal const Capability Publisher = Capability.QueuePublish | Capability.QueueInspect
        | Capability.QueueConsume | Capability.QueueAck;
    internal static IReadOnlyList<string> Nodes { get; } =
        [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
}
