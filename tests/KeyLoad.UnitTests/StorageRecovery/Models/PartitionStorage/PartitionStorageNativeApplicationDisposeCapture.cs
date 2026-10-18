using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>Borrowed actual services captured before their application provider is disposed.</summary>
internal sealed record PartitionStorageNativeApplicationDisposeCapture(
    PartitionHostApplicationOwner Owner, OrleansNode Node, ServerConnectionRegistry Connections,
    string Directory)
{
    internal static PartitionStorageNativeApplicationDisposeCapture Read(IServiceProvider services)
    {
        var owner = services.GetRequiredService<PartitionHostApplicationOwner>();
        return new(owner, services.GetRequiredService<OrleansNode>(),
            services.GetRequiredService<ServerConnectionRegistry>(), owner.Borrow().DirectoryPath);
    }
}
