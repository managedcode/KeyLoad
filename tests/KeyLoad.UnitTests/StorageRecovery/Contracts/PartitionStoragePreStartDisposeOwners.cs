using KeyLoad.Server;

namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed record PartitionStoragePreStartDisposeOwners(PartitionHostApplicationOwner Physical,
    OrleansNode Execution, ServerConnectionRegistry Connections);
