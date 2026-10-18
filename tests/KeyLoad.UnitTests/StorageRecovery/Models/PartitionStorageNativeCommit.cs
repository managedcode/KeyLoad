using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionStorageNativeCommit(CommitReceipt Receipt, StoredOutcome Entry, byte[] Bytes);
