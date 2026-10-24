namespace KeyLoad.IntegrationTests.StorageReplication;

internal sealed record StrongReadSessionCommit(CommandRequest Command, CommitReceipt Receipt);
