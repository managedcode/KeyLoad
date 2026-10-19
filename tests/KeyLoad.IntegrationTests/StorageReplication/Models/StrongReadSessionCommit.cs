namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal sealed record StrongReadSessionCommit(CommandRequest Command, CommitReceipt Receipt);
