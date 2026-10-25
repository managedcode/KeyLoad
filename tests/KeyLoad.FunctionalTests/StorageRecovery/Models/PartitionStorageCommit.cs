namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record PartitionStorageCommit(CommitReceipt Receipt, StoredOutcome Outcome, byte[] Bytes);
