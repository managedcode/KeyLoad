namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record ResourcePartitionInventoryRf3Original(
    CommandRequest Command, CommitReceipt Receipt, DocumentResult Document);
