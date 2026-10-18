using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeHosting
{
    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton(provider => new RemoteGraphNativeRuntime(provider.GetRequiredService<PartitionHostApplicationOwner>().Borrow(),
            provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(), provider.GetRequiredService<global::Orleans.Runtime.ISiloStatusOracle>(),
            provider.GetRequiredService<SiloStorageIdentityProvider>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<RemoteGraphNativeRuntime>>(),
            provider.GetRequiredService<Func<NativeRequestWorkOwner>>()));
        services.AddSingleton(provider => new RemoteGraphNativeClient(provider, provider.GetRequiredService<PartitionHostApplicationOwner>().Borrow(),
            provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(), provider.GetRequiredService<PartitionMovementGrainServiceClient>(),
            provider.GetRequiredService<SiloStorageIdentityProvider>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IRemoteGraphParent>(provider =>
        {
            var node = provider.GetRequiredService<OrleansNode>();
            node.RequireLocalRuntimePublished();
            return node.GraphResources.Runtime?.Parent!;
        });
    }
}
