namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextSelectedExplainPublicState(ResourceDefinition Resource,
    TextIndexMaintenanceRequest Build, TextIndexMaintenanceResult Built,
    TextIndexMaintenanceRequest Restore, TextIndexMaintenanceResult Restored,
    CommandRequest Published, CommitReceipt Publication, CommandRequest Deleted, CommitReceipt Deletion,
    OutboxStatus Outbox, NodeStatus Status);
