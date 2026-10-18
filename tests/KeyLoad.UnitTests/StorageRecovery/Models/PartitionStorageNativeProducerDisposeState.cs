using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record PartitionStorageNativeProducerDisposeState(PartitionHostApplicationOwner Owner,
    OrleansNode Node, ServerConnectionRegistry Connections, RecurringDueGrainService Recurring,
    SampleChunkGrainService Chunks, AnnProjectionGrainService Ann, StoreIdentity Identity, string Directory);
