using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.TestingHost;
using Orleans.Serialization;

namespace KeyLoad.RecoveryTests;

internal sealed class PeerDiscoveryNativeConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        var fixture = PeerDiscoveryNativeOwners.Resolve(silo.Configuration);
        var services = silo.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => fixture.CreateNode(
            provider.GetRequiredService<IOptions<SiloOptions>>().Value.SiloName,
            provider.GetRequiredService<ILocalSiloDetails>()));
        services.AddSingleton<IOptions<ReplicaConfiguration>>(provider => RecoveryExecutionOptions.Configuration(
            provider.GetRequiredService<PeerDiscoveryNativeNode>().Configuration));
        services.AddSingleton<IOptions<ReplicaPeerOptions>>(provider => Options.Create(provider.GetRequiredService<PeerDiscoveryNativeNode>().Peers));
        services.AddSingleton(Options.Create(new OrleansMembershipOptions()));
        services.AddSingleton(Options.Create(new ReplicaTransportOptions()));
        services.AddSingleton(provider => provider.GetRequiredService<PeerDiscoveryNativeNode>().Discovery);
        services.AddSingleton(provider => provider.GetRequiredService<PeerDiscoveryNativeNode>().Authentication);
        services.AddSingleton<IReplicaEndpoint>(provider => provider.GetRequiredService<PeerDiscoveryNativeNode>().Consensus);
        services.AddSingleton(provider =>
        {
            var node = provider.GetRequiredService<PeerDiscoveryNativeNode>();
            node.Runtime = new(provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), node.Discovery,
                node.Authentication, provider.GetRequiredService<ISiloStatusOracle>(),
                provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(), TimeProvider.System);
            return node.Runtime;
        });
        services.AddSingleton<ReplicaSiloDiscoveryClient>();
        services.AddSingleton<ReplicaGrainServiceClient>();
        services.AddSerializer(builder => builder.AddAssembly(typeof(ReplicaPeerEnvelope).Assembly));
        silo.AddGrainService<PartitionReplicaGrainService>();
    }
}
