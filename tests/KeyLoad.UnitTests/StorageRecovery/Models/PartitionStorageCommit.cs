namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed record PartitionStorageCommit(CommitReceipt Receipt, StoredOutcome Outcome, byte[] Bytes);
