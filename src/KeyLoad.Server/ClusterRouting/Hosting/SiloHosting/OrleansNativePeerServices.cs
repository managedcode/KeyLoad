using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansNativePeerServices
{
    internal static void RegisterSiloOwners(WebApplicationBuilder builder,
        NativeLocalhostClusteringOptions? localhostClustering)
    {
        var services = builder.Services;
        if (localhostClustering is null)
        {
            OrleansNativeClustering.RegisterHostingOptions(builder);
        }
        services.AddSingleton(provider => provider.GetRequiredService<OrleansNode>().ConnectionOwner);
        services.AddSingleton<Func<NativeRequestWorkOwner>>(provider =>
        {
            var borrowed = provider.GetRequiredService<OrleansNode>().RequestWork;
            return () => borrowed;
        });
        services.AddSingleton<PhysicalOwnerProbeNativeClient>(provider => new(provider));
    }

    internal static void RegisterReplicaDiscovery(IServiceCollection services)
    {
        services.AddSingleton<ReplicaNativeDiscoveryRuntime>(provider => new(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<SiloStorageIdentityProvider>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(),
            provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetService<IReplicaNativeDiscoveryObserver>()));
        services.AddSingleton<SiloStorageIdentityClient>(provider => new(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<SiloStorageIdentityProvider>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(),
            provider.GetRequiredService<TimeProvider>(), provider,
            provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(), provider.GetService<ReplicaDiscoveryProbe>()));
    }
}
