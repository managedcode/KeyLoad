using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansRequestServiceRegistration
{
    internal static void Register(IServiceCollection services, NodeOptions options)
    {
        if (options.RequestCqrsProbe.Enabled)
        {
            services.AddSingleton<IReplicaTransportObservation>(provider =>
            {
                _ = provider.GetRequiredService<RequestCqrsProbeObserver>();
                return provider.GetRequiredService<PartitionHostApplicationOwner>().Borrow().ApplyProbe;
            });
            services.AddSingleton(provider => AttachApplyObserver(provider.GetRequiredService<PartitionHostApplicationOwner>().Borrow(),
                RequestCqrsProbeObserverFactory.Create(options.RequestCqrsProbe,
                    provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), options.AllowPrivateNetworkHttp,
                    provider.GetRequiredService<ILocalSiloDetails>(), provider.GetRequiredService<IHostApplicationLifetime>(),
                    provider.GetRequiredService<IOptions<RequestProbeExecutionOptions>>(), provider.GetRequiredService<TimeProvider>())
                    ?? throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidOptions)));
            services.AddSingleton<Func<IGrainRequestPhaseObserver>>(provider =>
            {
                var borrowed = provider.GetRequiredService<RequestCqrsProbeObserver>();
                return () => borrowed;
            });
            services.AddSingleton<IQueryReadCutObserver>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>().QueryCuts);
            services.AddSingleton<Func<global::KeyLoad.Server.Features.DocumentStorage.IRemoteModelNativeFaultObserver>>(provider =>
            {
                var borrowed = provider.GetRequiredService<RequestCqrsProbeObserver>();
                return () => borrowed;
            });
            services.AddSingleton<Func<global::KeyLoad.Server.Features.DocumentStorage.IRemoteModelNativeReplayObserver>>(provider =>
            {
                var borrowed = provider.GetRequiredService<RequestCqrsProbeObserver>();
                return () => borrowed;
            });
            services.AddSingleton(provider => new RequestCqrsProbeNativeRelay(provider,
                provider.GetRequiredService<IOptions<NodeOptions>>(), provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
                provider.GetRequiredService<ILocalSiloDetails>(), provider.GetRequiredService<ISiloStatusOracle>(),
                provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
                provider.GetRequiredService<IOptions<RequestProbeExecutionOptions>>(), provider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IGrainActivationMigrationObserver>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>()
                .CreateMigration(provider.GetRequiredService<ReplicaSiloDiscoveryClient>()));
        }
        services.AddSingleton(provider => new GrainRequestCodec(provider.GetRequiredService<DatabaseEngine>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<GrainRoutingOptions>>())
        {
            PhaseObserver = provider.GetService<Func<IGrainRequestPhaseObserver>>()?.Invoke()
                ?? provider.GetService<IGrainRequestPhaseObserver>(),
            PurposeObserver = provider.GetRequiredService<OrleansNode>().DiscoveryOmission?.PurposeObserver
                ?? provider.GetService<IGrainRequestPurposeObserver>()
        });
    }

    private static RequestCqrsProbeObserver AttachApplyObserver(PartitionHost partition, RequestCqrsProbeObserver observer)
    {
        partition.ApplyProbe.Attach(observer);
        return observer;
    }
}
