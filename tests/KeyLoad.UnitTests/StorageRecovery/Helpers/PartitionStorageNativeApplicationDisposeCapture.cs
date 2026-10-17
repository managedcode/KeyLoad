using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Services;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Borrowed actual services captured before their application provider is disposed.</summary>
internal sealed record PartitionStorageNativeApplicationDisposeCapture(
    PartitionHostApplicationOwner Owner, OrleansNode Node, ServerConnectionRegistry Connections,
    ReplicaTransportLifecycle Transport, RecurringDueGrainService Recurring,
    SampleChunkGrainService Chunks, AnnProjectionGrainService Ann, string Directory)
{
    internal static PartitionStorageNativeApplicationDisposeCapture Read(IServiceProvider services)
    {
        var native = services.GetServices<IGrainService>().ToArray();
        var owner = services.GetRequiredService<PartitionHostApplicationOwner>();
        return new(owner, services.GetRequiredService<OrleansNode>(),
            services.GetRequiredService<ServerConnectionRegistry>(),
            services.GetRequiredService<ReplicaTransportLifecycle>(),
            native.OfType<RecurringDueGrainService>().Single(),
            native.OfType<SampleChunkGrainService>().Single(),
            native.OfType<AnnProjectionGrainService>().Single(), owner.Borrow().DirectoryPath);
    }
}
