using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Protocol
{
    internal const string TenantPrefix = "producer-rf3-";
    internal const string Database = "producer-db";
    internal const string Domain = "producer-work";
    internal const string Queue = "jobs";
    internal const string Collection = "producers";
    internal const string StreamSet = "producer-events";
    internal const string ForeignStreamSet = "foreign-events";
    internal const string ForeignDomain = "foreign-producer-domain";
    internal const string EventType = "Produced";
    internal const long OriginalEventSequence = 1;
    internal const long HealthyEventSequence = 2;
    internal const long HealthyReadySequence = 2;
    internal const long EmptyStreamRevision = 0;
    internal const long InitialEventRevision = 1;
    internal const long FirstAvailableEventRevision = 1;
    internal const long InitialEventGeneration = 1;
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
    internal const long AbsentDocumentRevision = 0;
    internal const string DocumentRevisionRefusalDetail = "The expected revision does not match.";
    internal const int OriginalMutationCount = 4;
    internal const int InitialAttempts = 0;
    internal const long UnclaimedLeaseVersion = 0;
    internal const long InitialGeneration = 1;
    internal const long InitialReadySequence = 1;
    internal const long ScheduledReadySequence = 0;
    internal const Capability Publisher = Capability.QueuePublish | Capability.QueueInspect
        | Capability.QueueConsume | Capability.QueueAck | Capability.Query;
    internal static IReadOnlyList<string> Nodes { get; } =
        [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
}
