using System.Collections.Immutable;
using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Orleans.Configuration;
using Orleans.Serialization;

namespace KeyLoad.Server;

internal static class OrleansSiloConfiguration
{
    internal static IHost Build(PartitionHost partition, NodeOptions options, INodeAdministration administration,
        ILoggerFactory loggerFactory, NativeRequestWorkOwner requestWork, IPAddress address,
        CancellationToken startupCancellation)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(loggerFactory);
        RegisterBorrowedServices(builder.Services, partition, administration, options, requestWork, startupCancellation);
        builder.UseOrleans(silo => Configure(silo, options, partition.Configuration, address));
        return builder.Build();
    }

    private static void RegisterBorrowedServices(IServiceCollection services, PartitionHost partition,
        INodeAdministration administration, NodeOptions options, NativeRequestWorkOwner requestWork,
        CancellationToken startupCancellation)
    {
        var peers = options.CreatePeerOptions();
        peers.Validate(partition.Configuration);
        var expectedOwner = new PhysicalShardRecord(options.PhysicalShardId, partition.Configuration.Incarnation,
            ImmutableArray.CreateRange(partition.Configuration.VoterIds),
            PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch);
        services.AddSingleton(expectedOwner);
        services.AddSingleton(partition.Database);
        services.AddSingleton<ICommitCoordinator>(partition.Coordinator);
        services.AddSingleton<IReplicaEndpoint>(partition.Consensus);
        services.AddSingleton(partition.Consensus);
        services.AddSingleton(partition.Configuration);
        services.AddSingleton(peers);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(requestWork);
        services.AddSingleton(administration);
        services.AddSingleton<QueryEngine>();
        services.AddSingleton(_ => new SearchEngine(partition.Database, partition.TextProjection));
        RegisterRequestCodec(services, partition, options);
        services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestProgress).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        services.AddSingleton<ReplicaSiloDiscoveryState>();
        services.AddSingleton(provider => new ReplicaEnvelopeAuthenticator(partition.Configuration, peers,
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), TimeProvider.System,
            logger: provider.GetService<ILogger<ReplicaEnvelopeAuthenticator>>(), canonicalDatabase: partition.Database));
        services.AddSingleton<ReplicaSiloDiscoveryClient>(provider => new ReplicaSiloDiscoveryClient(
            partition.Configuration, peers, provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(), TimeProvider.System,
            options.RequestCqrsProbe.DiscoveryCaptureMode == RequestCqrsProbeProtocol.MixedInterface3Capture
                ? provider.GetRequiredService<IReplicaDiscoveryObservationSink>() : null));
        services.AddSingleton<ReplicaGrainServiceClient>();
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, ReplicaTransportLifecycle>();
        services.AddSingleton<IMembershipTable>(new ReplicaMembershipTable(partition.Database, partition.Coordinator,
            partition.Consensus, options.ClusterId, ClusterPrincipalPolicy.InternalPrincipalId, TimeProvider.System,
            startupCancellation));
    }

    private static void RegisterRequestCodec(IServiceCollection services, PartitionHost partition, NodeOptions options)
    {
        if (options.RequestCqrsProbe.Enabled)
        {
            services.AddSingleton(provider => RequestCqrsProbeObserverFactory.Create(
                options.RequestCqrsProbe, partition.Configuration, options.AllowPrivateNetworkHttp,
                provider.GetRequiredService<ILocalSiloDetails>(), provider.GetRequiredService<IHostApplicationLifetime>())
                ?? throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidOptions));
            services.AddSingleton<IGrainRequestPhaseObserver>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>());
            if (options.RequestCqrsProbe.DiscoveryCaptureMode == RequestCqrsProbeProtocol.MixedInterface3Capture)
            { services.AddSingleton<IReplicaDiscoveryObservationSink>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>()); }
        }
        services.AddSingleton(provider => new GrainRequestCodec(partition.Database, TimeProvider.System)
        {
            PhaseObserver = provider.GetService<IGrainRequestPhaseObserver>()
        });
    }

    private static void Configure(ISiloBuilder silo, NodeOptions options, ReplicaConfiguration replica, IPAddress address)
    {
        silo.Configure<ClusterOptions>(cluster =>
        {
            cluster.ClusterId = options.ClusterId;
            cluster.ServiceId = OrleansNodeProtocol.ServiceId;
        });
        silo.ConfigureEndpoints(address, options.SiloPort, OrleansNodeProtocol.GatewayPort);
        silo.Configure<ClusterMembershipOptions>(membership =>
        {
            membership.IAmAliveTablePublishTimeout = OrleansNodeProtocol.MembershipRefresh;
            membership.TableRefreshTimeout = OrleansNodeProtocol.MembershipRefresh;
        });
        silo.Configure<SiloMessagingOptions>(messaging => messaging.MaxMessageBodySize = Math.Max(
            checked(replica.MaxAppendBytes + ReplicaTransportProtocol.MaximumMetadataBytes
                + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes),
            checked(GrainRequestStreamProtocol.MaximumCompletedBytes + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes)));
        silo.AddGrainService<PartitionReplicaGrainService>();
        silo.AddGrainService<RecurringDueGrainService>();
        // ADR-036: owner explicitly requires these two native experimental services.
#pragma warning disable ORLEANSEXP003
        silo.AddDistributedGrainDirectory();
#pragma warning restore ORLEANSEXP003
#pragma warning disable ORLEANSEXP001
        silo.AddActivationRepartitioner();
#pragma warning restore ORLEANSEXP001
        silo.AddOrleansGraph(configureGraph: graph => graph.AllowClientCallGrain<IRequestGrain>()
            .AddGrainServiceTransition<RecurringDueGrainService, IRecurringDueCoordinatorGrain>(
                nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync))
            .AddGrainTransition<IRecurringDueCoordinatorGrain, IRequestGrain>()
            .MethodByName(nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync), nameof(IRequestGrain.ExecuteStreamAsync)).And()
            .AddGrainTransition<IRequestGrain, IDatabaseReadGrain>()
            .MethodByName(nameof(IRequestGrain.ExecuteStreamAsync), nameof(IDatabaseReadGrain.ExecuteAsync)).And()
            .AddGrainTransition<IRequestGrain, ICommandPartitionGrain>()
            .MethodByName(nameof(IRequestGrain.ExecuteStreamAsync), nameof(ICommandPartitionGrain.ExecuteAsync)).And());
    }
}
