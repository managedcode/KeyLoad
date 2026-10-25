using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record PartitionStorageProducerDisposeState(PartitionHostApplicationOwner Owner,
    OrleansNode Node, ServerConnectionRegistry Connections, StoreIdentity Identity, string Directory);
