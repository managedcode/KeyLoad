using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionStorageNativeProducerDisposeState(PartitionHostApplicationOwner Owner,
    OrleansNode Node, ServerConnectionRegistry Connections, StoreIdentity Identity, string Directory);
