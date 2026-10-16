using KeyLoad.Clustering;
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
        services.AddSingleton<ReplicaTransportLifecycle>();
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider =>
            provider.GetRequiredService<ReplicaTransportLifecycle>());
        services.AddSingleton(provider => fixture.CreateNode(
            provider.GetRequiredService<IOptions<SiloOptions>>().Value.SiloName,
            provider.GetRequiredService<ILocalSiloDetails>(), provider));
        services.AddSingleton<IOptions<ReplicaConfiguration>>(provider => UnitExecutionOptions.ReplicaConfiguration(
            provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.Configuration));
        services.AddSingleton<IOptions<ReplicaPeerOptions>>(provider => UnitRoutingOptions.Peers(
            provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.Configuration,
            provider.GetRequiredService<RequestCqrsCohortNativeNode>().Peers));
        if (fixture.HasBarrierDeadline)
        {
            services.AddSingleton<IReplicaTransportObservation>(provider =>
                provider.GetRequiredService<RequestCqrsCohortNativeNode>().BarrierObservation
                ?? throw new InvalidOperationException("The native barrier observation owner is absent."));
        }
        services.AddSingleton(UnitRoutingOptions.Membership());
        services.AddSingleton(UnitRoutingOptions.Transport());
        services.AddSingleton(provider => provider.GetRequiredService<RequestCqrsCohortNativeNode>().Discovery);
        services.AddSingleton<Func<ReplicaEnvelopeAuthenticator>>(provider =>
        {
            var authentication = provider.GetRequiredService<RequestCqrsCohortNativeNode>().Authentication;
            return () => authentication;
        });
        services.AddSingleton<Func<IReplicaEndpoint>>(provider =>
        {
            var endpoint = provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.Consensus;
            return () => endpoint;
        });
        services.AddSingleton<ReplicaNativeDiscoveryRuntime>(provider =>
        {
            var node = provider.GetRequiredService<RequestCqrsCohortNativeNode>();
            node.Runtime = new(provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
                node.Discovery, node.Authentication, provider.GetRequiredService<ISiloStatusOracle>(),
                provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(), TimeProvider.System, node.Observation);
            node.Observation.Runtime = node.Runtime;
            return node.Runtime!;
        });
        services.AddSingleton(provider => new ReplicaSiloDiscoveryClient(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), provider.GetRequiredService<Func<ReplicaEnvelopeAuthenticator>>()(),
            provider.GetRequiredService<TimeProvider>(), provider, provider.GetRequiredService<ISiloStatusOracle>(),
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>()));
        services.AddSingleton(provider => new ReplicaGrainServiceClient(provider,
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), provider.GetRequiredService<ReplicaSiloDiscoveryClient>(),
            provider.GetRequiredService<Func<ReplicaEnvelopeAuthenticator>>()(),
            provider.GetRequiredService<IOptions<ReplicaTransportOptions>>(),
            provider.GetRequiredService<RequestCqrsCohortNativeNode>().Journal.ExecutionOptions,
            provider.GetRequiredService<TimeProvider>()));
        services.AddSerializer(builder => builder.AddAssembly(typeof(ReplicaPeerEnvelope).Assembly));
        silo.AddGrainService<PartitionReplicaGrainService>();
    }
}
