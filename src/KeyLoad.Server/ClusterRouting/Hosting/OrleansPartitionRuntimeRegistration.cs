using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.GraphTraversal;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansPartitionRuntimeRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton(provider => new PartitionMovementGrainServiceClient(provider, Host(provider),
            provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(), provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new RemoteModelNativeRuntime(Host(provider),
            provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(), provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<RemoteModelNativeRuntime>>(),
            provider.GetRequiredService<Func<NativeRequestWorkOwner>>()));
        services.AddSingleton(provider => new RemoteModelNativeClient(provider, Host(provider),
            provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(), provider.GetRequiredService<PartitionMovementGrainServiceClient>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new NativeCommandObservationTarget(Host(provider).Database,
            provider.GetRequiredService<Func<ICommitCoordinator>>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>(),
            provider.GetRequiredService<Func<NativeRequestWorkOwner>>()(),
            provider.GetRequiredService<IPhysicalRequestPlacement>(), provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<GrainRequestCodec>()));
        services.AddSingleton(provider => new CommandObservationNativeEndpoint(Host(provider),
            provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<PartitionMovementGrainServiceClient>(),
            provider.GetRequiredService<NativeCommandObservationTarget>()));
        services.AddSingleton<INativeCommandObservationTransport>(provider => new CommandObservationNativeTransport(Host(provider),
            provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<PartitionMovementGrainServiceClient>()));
        RemoteGraphNativeHosting.Register(services);
        RegisterDiscovery(services);
    }

    private static void RegisterDiscovery(IServiceCollection services)
    {
        services.AddSingleton(provider => new ReplicaSiloDiscoveryState(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ILocalSiloDetails>(), RuntimeJournalStorePreparation.CurrentCapabilityEvidence(Host(provider))));
        services.AddSingleton(provider => new ReplicaEnvelopeAuthenticator(provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(), provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<ReplicaTransportOptions>>(),
            provider.GetRequiredService<IOptions<ReplicaReplayLimits>>(), logger: provider.GetService<ILogger<ReplicaEnvelopeAuthenticator>>(),
            canonicalDatabase: Host(provider).Database));
        services.AddSingleton<Func<ReplicaEnvelopeAuthenticator>>(provider =>
        {
            var borrowed = provider.GetRequiredService<ReplicaEnvelopeAuthenticator>();
            return () => borrowed;
        });
        OrleansNativePeerServices.RegisterReplicaDiscovery(services);
        services.AddSingleton<ReplicaGrainServiceClient>();
    }

    private static PartitionHost Host(IServiceProvider services) => services.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
}
