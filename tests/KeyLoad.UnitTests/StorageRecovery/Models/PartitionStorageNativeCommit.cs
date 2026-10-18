
namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionStorageNativeCommit(CommitReceipt Receipt, ReplicaEntry Entry, byte[] Bytes);
