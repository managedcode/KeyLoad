using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionPairedIdentityCut(StoreIdentity Canonical, StoreIdentity Replica,
    long CanonicalPosition, long ReplicaPosition, long JournalLength);
