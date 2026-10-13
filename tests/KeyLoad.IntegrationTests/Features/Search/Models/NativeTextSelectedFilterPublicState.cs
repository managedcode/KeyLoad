namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextSelectedFilterPublicState(ResourceDefinition Resource,
    CommandRequest Published, CommitReceipt Publication, TextIndexMaintenanceRequest Build,
    TextIndexMaintenanceResult Built, OutboxStatus Outbox, NodeStatus Status);
