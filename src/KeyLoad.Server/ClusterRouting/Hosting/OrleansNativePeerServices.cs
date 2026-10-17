using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansNativePeerServices
{
    internal static void RegisterSiloOwners(WebApplicationBuilder builder, NodeOptions options,
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
        if (options.NativeDiscoveryOmission.Enabled)
        {
            services.AddSingleton<IReplicaNativeDiscoveryObserver>(provider =>
                provider.GetRequiredService<OrleansNode>().DiscoveryOmission!);
            services.AddSingleton(provider => provider.GetRequiredService<OrleansNode>().DiscoveryOmission!.CreateBorrowedProbe());
        }
        RegisterObservers(services, services.LastOrDefault(registration => registration.ServiceType
            == typeof(IGrainPartitionMovementSealedOperationObserver))?.ImplementationInstance as IGrainPartitionMovementSealedOperationObserver);
        services.AddSingleton<PhysicalOwnerProbeNativeClient>(provider => new(provider));
    }

    internal static void RegisterObservers(IServiceCollection services, IGrainPartitionMovementSealedOperationObserver? observer)
    {
        if (observer is null)
        {
            return;
        }
        if (observer is IGrainRequestPurposeObserver purpose
            && !services.Any(registration => registration.ServiceType == typeof(IGrainRequestPurposeObserver)))
        {
            services.AddSingleton(purpose);
        }
        if (observer is IGrainRequestPhaseObserver phase
            && !services.Any(registration => registration.ServiceType == typeof(IGrainRequestPhaseObserver)))
        {
            services.AddSingleton(phase);
        }
    }

    internal static void RegisterReplicaDiscovery(IServiceCollection services)
    {
        services.AddSingleton<ReplicaNativeDiscoveryRuntime>(provider => new(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(),
            provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetService<IReplicaNativeDiscoveryObserver>()));
        services.AddSingleton<ReplicaSiloDiscoveryClient>(provider => new(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(),
            provider.GetRequiredService<TimeProvider>(), provider,
            provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(), provider.GetService<ReplicaDiscoveryProbe>()));
    }
}
