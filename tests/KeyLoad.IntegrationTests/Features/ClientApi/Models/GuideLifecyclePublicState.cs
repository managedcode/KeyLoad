using KeyLoad.IntegrationTests.Features.DocumentStorage;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal sealed record GuideLifecyclePublicState(McpDocumentScenario Scenario, McpPersistedIdentity Identity,
    AtomicPartitionPlacementResolution Placement, CommandRequest Seed, CommitReceipt SeedReceipt,
    CommandRequest Healthy, OutboxStatus Outbox)
{
    internal Guid RepairCommandId { get; } = Guid.NewGuid();
    internal Result? RepairOutcome { get; set; }
    internal CommitReceipt? HealthyReceipt { get; set; }
    internal OutboxStatus CurrentOutbox { get; set; } = Outbox;
}
