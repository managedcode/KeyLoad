using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionStoragePreStartDisposeOwners(PartitionHostApplicationOwner Physical,
    OrleansNode Execution, ServerConnectionRegistry Connections);
