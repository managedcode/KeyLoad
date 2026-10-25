using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record PartitionStoragePreStartDisposeOwners(PartitionHostApplicationOwner Physical,
    OrleansNode Execution, ServerConnectionRegistry Connections);
