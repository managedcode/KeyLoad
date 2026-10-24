using KeyLoad.Storage;

namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed record PartitionIdentityCut(StoreIdentity Identity, long Position, long JournalLength);
