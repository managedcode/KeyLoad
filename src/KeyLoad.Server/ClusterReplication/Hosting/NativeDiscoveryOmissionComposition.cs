using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class NativeDiscoveryOmissionComposition
{
    internal static OrleansNode CreateNode(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<NodeOptions>>();
        if (options.Value.NativeDiscoveryOmission.Enabled)
        {
            var owner = new NativeDiscoveryOmissionOwner(options,
                provider.GetRequiredService<IOptions<KeyLoad.Server.Features.ClusterRouting.RequestProbeExecutionOptions>>(),
                provider.GetRequiredService<IHttpContextAccessor>());
            owner.AttachPurposeObserver(provider.GetRequiredService<IOptions<KeyLoad.Server.Features.ClusterRouting.RequestProbeExecutionOptions>>());
            return ActivatorUtilities.CreateInstance<OrleansNode>(provider, owner);
        }
        return ActivatorUtilities.CreateInstance<OrleansNode>(provider);
    }
}
