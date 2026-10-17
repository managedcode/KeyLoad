namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextPartitionGenerationPublicState(PartitionRef Partition, string Json,
    CommandRequest Command, CommitReceipt Receipt, TextIndexMaintenanceRequest Build,
    TextIndexMaintenanceResult Built, OutboxStatus Outbox);
