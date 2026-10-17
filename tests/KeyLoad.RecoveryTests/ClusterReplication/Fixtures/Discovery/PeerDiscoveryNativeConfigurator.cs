using KeyLoad.Clustering;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.RecoveryTests;

internal sealed class PeerDiscoveryNativeConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        var fixture = PeerDiscoveryNativeOwners.Resolve(silo.Configuration);
        var services = silo.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ReplicaTransportLifecycle>();
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider =>
            provider.GetRequiredService<ReplicaTransportLifecycle>());
        services.AddSingleton(provider => fixture.CreateNode(
            provider.GetRequiredService<IOptions<SiloOptions>>().Value.SiloName,
            provider.GetRequiredService<ILocalSiloDetails>()));
        services.AddSingleton<IOptions<ReplicaConfiguration>>(provider => RecoveryExecutionOptions.Configuration(
            provider.GetRequiredService<PeerDiscoveryNativeNode>().Configuration));
        services.AddSingleton<IOptions<ReplicaPeerOptions>>(provider => Options.Create(provider.GetRequiredService<PeerDiscoveryNativeNode>().Peers));
        services.AddSingleton(Options.Create(new OrleansMembershipOptions()));
        services.AddSingleton(Options.Create(new ReplicaTransportOptions()));
        services.AddSingleton(provider => provider.GetRequiredService<PeerDiscoveryNativeNode>().Discovery);
        services.AddSingleton<Func<ReplicaEnvelopeAuthenticator>>(provider =>
        {
            var authentication = provider.GetRequiredService<PeerDiscoveryNativeNode>().Authentication;
            return () => authentication;
        });
        services.AddSingleton<Func<IReplicaEndpoint>>(provider =>
        {
            var endpoint = provider.GetRequiredService<PeerDiscoveryNativeNode>().Consensus;
            return () => endpoint;
        });
        services.AddSingleton(provider =>
        {
            var node = provider.GetRequiredService<PeerDiscoveryNativeNode>();
            node.Runtime = new(provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), node.Discovery,
                node.Authentication, provider.GetRequiredService<ISiloStatusOracle>(),
                provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(), TimeProvider.System);
            return node.Runtime;
        });
        services.AddSingleton(provider => new ReplicaSiloDiscoveryClient(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), provider.GetRequiredService<Func<ReplicaEnvelopeAuthenticator>>()(),
            provider.GetRequiredService<TimeProvider>(), provider, provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>()));
        services.AddSingleton(provider => new ReplicaGrainServiceClient(provider,
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), provider.GetRequiredService<ReplicaSiloDiscoveryClient>(),
            provider.GetRequiredService<Func<ReplicaEnvelopeAuthenticator>>()(),
            provider.GetRequiredService<IOptions<ReplicaTransportOptions>>(), RecoveryExecutionOptions.Replica(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSerializer(builder => builder.AddAssembly(typeof(ReplicaPeerEnvelope).Assembly));
        silo.AddGrainService<PartitionReplicaGrainService>();
    }
}
