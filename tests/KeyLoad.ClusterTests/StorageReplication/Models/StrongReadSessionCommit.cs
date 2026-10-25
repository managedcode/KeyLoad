namespace KeyLoad.ClusterTests.StorageReplication;

internal sealed record StrongReadSessionCommit(CommandRequest Command, CommitReceipt Receipt);
