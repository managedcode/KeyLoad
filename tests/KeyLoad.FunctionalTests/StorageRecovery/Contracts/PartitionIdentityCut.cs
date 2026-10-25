using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record PartitionIdentityCut(StoreIdentity Identity, long Position, long JournalLength);
