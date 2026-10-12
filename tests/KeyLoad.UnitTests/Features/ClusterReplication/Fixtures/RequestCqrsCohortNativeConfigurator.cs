using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortNativeConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        var fixture = RequestCqrsCohortOwners.Resolve(silo.Configuration);
        var services = silo.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => fixture.CreateNode(
            provider.GetRequiredService<IOptions<SiloOptions>>().Value.SiloName,
            provider.GetRequiredService<ILocalSiloDetails>()));
        services.AddSingleton<IOptions<ReplicaConfiguration>>(provider => UnitExecutionOptions.ReplicaConfiguration(
            provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.Configuration));
        services.AddSingleton<IOptions<ReplicaPeerOptions>>(provider => UnitRoutingOptions.Peers(
            provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.Configuration,
            provider.GetRequiredService<RequestCqrsCohortNativeNode>().Peers));
        services.AddSingleton(UnitRoutingOptions.Membership());
        services.AddSingleton(UnitRoutingOptions.Transport());
        services.AddSingleton(provider => provider.GetRequiredService<RequestCqrsCohortNativeNode>().Discovery);
        services.AddSingleton(provider => provider.GetRequiredService<RequestCqrsCohortNativeNode>().Authentication);
        services.AddSingleton<IReplicaEndpoint>(provider => provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.Consensus);
        services.AddSingleton<ReplicaNativeDiscoveryRuntime>(provider =>
        {
            var node = provider.GetRequiredService<RequestCqrsCohortNativeNode>();
            node.Runtime = new(provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
                node.Discovery, node.Authentication, provider.GetRequiredService<ISiloStatusOracle>(),
                provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(), TimeProvider.System, node.Observation);
            node.Observation.Runtime = node.Runtime;
            return node.Runtime!;
        });
        services.AddSingleton<ReplicaSiloDiscoveryClient>();
        services.AddSingleton<ReplicaGrainServiceClient>();
        services.AddSerializer(builder => builder.AddAssembly(typeof(ReplicaPeerEnvelope).Assembly));
        silo.AddGrainService<PartitionReplicaGrainService>();
    }
}
