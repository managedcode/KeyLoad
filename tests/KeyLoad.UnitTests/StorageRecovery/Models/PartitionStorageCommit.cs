namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionStorageCommit(CommitReceipt Receipt, StoredOutcome Outcome, byte[] Bytes);
