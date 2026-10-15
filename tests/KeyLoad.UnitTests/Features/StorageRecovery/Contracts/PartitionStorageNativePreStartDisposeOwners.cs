using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionStorageNativePreStartDisposeOwners(PartitionHostApplicationOwner Physical,
    OrleansNode Native, ServerConnectionRegistry Connections, ReplicaTransportLifecycle Transport);
