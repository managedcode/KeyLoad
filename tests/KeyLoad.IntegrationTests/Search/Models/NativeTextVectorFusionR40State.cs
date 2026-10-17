using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextVectorFusionR40State(NativeTextRf3Scenario Scenario,
    CommandRequest Command, CommitReceipt Receipt, AtomicPartitionPlacementResolution Placement,
    TextIndexMaintenanceRequest Build, TextIndexMaintenanceResult Built, McpPersistedIdentity Reader,
    OutboxStatus Outbox);
