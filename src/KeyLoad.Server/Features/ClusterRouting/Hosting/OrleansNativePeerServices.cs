using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansNativePeerServices
{
    internal static void RegisterSiloOwners(HostApplicationBuilder builder, ILoggerFactory loggerFactory,
        NativeConnectionOwnerIdentity connectionOwner, NativeDiscoveryOmissionOwner? discoveryOmission,
        IGrainPartitionMovementSealedOperationObserver? sealedObserver, NativeLocalhostClusteringOptions? localhostClustering)
    {
        builder.Services.AddSingleton(loggerFactory);
        if (localhostClustering is null)
        {
            OrleansNativeClustering.RegisterHostingOptions(builder);
        }
        builder.Services.AddSingleton(connectionOwner);
        if (discoveryOmission is not null)
        {
            builder.Services.AddSingleton<IReplicaNativeDiscoveryObserver>(discoveryOmission);
            builder.Services.AddSingleton(discoveryOmission.CreateBorrowedProbe());
            if (discoveryOmission.PurposeObserver is { } purposeObserver)
            { builder.Services.AddSingleton(purposeObserver); }
        }
        if (sealedObserver is not null)
        { builder.Services.AddSingleton(sealedObserver); }
    }

    internal static void RegisterReplicaDiscovery(IServiceCollection services)
    {
        services.AddSingleton<ReplicaNativeDiscoveryRuntime>(provider => new(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(),
            provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IReplicaNativeDiscoveryObserver>()));
        services.AddSingleton<ReplicaSiloDiscoveryClient>(provider => new(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(),
            provider.GetRequiredService<TimeProvider>(), provider,
            provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetService<ReplicaDiscoveryProbe>()));
    }

    internal static void RegisterPhysicalProbe(IServiceCollection services, PhysicalOwnerProbeNativeRuntime? runtime)
    {
        if (runtime is not null)
        { services.AddSingleton(runtime); }
        services.AddSingleton<PhysicalOwnerProbeNativeClient>(provider => new(provider));
    }
}
