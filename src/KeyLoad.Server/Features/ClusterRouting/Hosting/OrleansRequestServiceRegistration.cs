using System.Collections.Immutable;
using System.Net;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Serialization;

namespace KeyLoad.Server;

internal static class OrleansRequestServiceRegistration
{
    internal static void Register(IServiceCollection services, PartitionHost partition, NodeOptions options)
    {
        if (options.RequestCqrsProbe.Enabled)
        {
            services.AddSingleton<IReplicaTransportObservation>(provider =>
            {
                _ = provider.GetRequiredService<RequestCqrsProbeObserver>();
                return partition.ApplyProbe;
            });
            services.AddSingleton(provider => AttachApplyObserver(partition, RequestCqrsProbeObserverFactory.Create(
                options.RequestCqrsProbe, provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), options.AllowPrivateNetworkHttp,
                provider.GetRequiredService<ILocalSiloDetails>(), provider.GetRequiredService<IHostApplicationLifetime>(),
                provider.GetRequiredService<IOptions<RequestProbeExecutionOptions>>(), provider.GetRequiredService<TimeProvider>())
                ?? throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidOptions)));
            services.AddSingleton<IGrainRequestPhaseObserver>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>());
            services.AddSingleton<IQueryReadCutObserver>(provider =>
                provider.GetRequiredService<RequestCqrsProbeObserver>().QueryCuts);
            services.AddSingleton<global::KeyLoad.Server.Features.DocumentStorage.IRemoteModelNativeFaultObserver>(
                provider => provider.GetRequiredService<RequestCqrsProbeObserver>());
            services.AddSingleton<global::KeyLoad.Server.Features.DocumentStorage.IRemoteModelNativeReplayObserver>(
                provider => provider.GetRequiredService<RequestCqrsProbeObserver>());
            services.AddSingleton<IGrainRequestProducerDisposedObserver>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>());
            services.AddSingleton(provider => new RequestCqrsProbeNativeRelay(provider,
                provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
                provider.GetRequiredService<ILocalSiloDetails>(), provider.GetRequiredService<ISiloStatusOracle>(),
                provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
                provider.GetRequiredService<IOptions<RequestProbeExecutionOptions>>(), provider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IGrainActivationMigrationObserver>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>()
                .CreateMigration(provider.GetRequiredService<ReplicaSiloDiscoveryClient>()));
        }
        services.AddSingleton(provider => new GrainRequestCodec(partition.Database, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>())
        {
            PhaseObserver = provider.GetService<IGrainRequestPhaseObserver>(),
            PurposeObserver = provider.GetService<IGrainRequestPurposeObserver>()
        });
    }

    private static RequestCqrsProbeObserver AttachApplyObserver(PartitionHost partition, RequestCqrsProbeObserver observer)
    {
        partition.ApplyProbe.Attach(observer);
        return observer;
    }
}
