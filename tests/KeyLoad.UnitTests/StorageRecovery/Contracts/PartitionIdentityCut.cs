using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionIdentityCut(StoreIdentity Identity, long Position, long JournalLength);
