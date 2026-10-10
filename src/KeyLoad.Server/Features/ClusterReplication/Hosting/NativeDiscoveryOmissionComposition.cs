using KeyLoad.Server.Features.BlobStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class NativeDiscoveryOmissionComposition
{
    internal static OrleansNode CreateNode(IServiceProvider provider, IControlledBlobWireBorrowObserver? blobObserver)
    {
        var options = provider.GetRequiredService<IOptions<NodeOptions>>();
        if (options.Value.NativeDiscoveryOmission.Enabled)
        {
            var owner = new NativeDiscoveryOmissionOwner(options,
                provider.GetRequiredService<IOptions<KeyLoad.Server.Features.ClusterRouting.RequestProbeExecutionOptions>>(),
                provider.GetRequiredService<IHttpContextAccessor>());
            return blobObserver is null ? ActivatorUtilities.CreateInstance<OrleansNode>(provider, owner)
                : ActivatorUtilities.CreateInstance<OrleansNode>(provider, owner, blobObserver);
        }
        return blobObserver is null ? ActivatorUtilities.CreateInstance<OrleansNode>(provider)
            : ActivatorUtilities.CreateInstance<OrleansNode>(provider, blobObserver);
    }
}
