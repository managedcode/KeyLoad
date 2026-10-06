using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication.Orleans.Converters;
using ManagedCode.Orleans.Graph.Extensions;
using ManagedCode.Orleans.Identity.Core.Serializations;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Serialization;

namespace KeyLoad.Server;

internal static class OrleansSiloConfiguration
{
    internal static IHost Build(PartitionHost partition, NodeOptions options, INodeAdministration administration,
        ILoggerFactory loggerFactory, NativeRequestWorkOwner requestWork, IPAddress address,
        ServerRuntimeOptions runtimeOptions, TimeProvider clock, CancellationToken startupCancellation)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(loggerFactory);
        runtimeOptions.RegisterBorrowed(builder.Services);
        RegisterBorrowedServices(builder.Services, partition, administration, options, requestWork, runtimeOptions, clock, startupCancellation);
        builder.UseOrleans(silo => Configure(silo, options, partition.Configuration, address, runtimeOptions.Membership.Value,
            runtimeOptions.Core.RuntimeJournal, runtimeOptions.DurableJobs, runtimeOptions.GrainRouting));
        return builder.Build();
    }

    private static void RegisterBorrowedServices(IServiceCollection services, PartitionHost partition,
        INodeAdministration administration, NodeOptions options, NativeRequestWorkOwner requestWork,
        ServerRuntimeOptions runtimeOptions, TimeProvider clock, CancellationToken startupCancellation)
    {
        var peers = runtimeOptions.Peer.Value;
        peers.Validate(partition.Configuration);
        var expectedOwner = new PhysicalShardRecord(options.PhysicalShardId, partition.Configuration.Incarnation,
            ImmutableArray.CreateRange(partition.Configuration.VoterIds),
            PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch);
        services.AddSingleton(expectedOwner);
        services.AddSingleton(partition.Database);
        services.AddSingleton<ICommitCoordinator>(partition.Coordinator);
        services.AddSingleton<IReplicaEndpoint>(partition.Consensus);
        services.AddSingleton(partition.Consensus);
        services.AddSingleton(clock);
        services.AddSingleton(requestWork);
        services.AddSingleton(administration);
        services.AddSingleton<QueryEngine>();
        services.AddSingleton(_ => new SearchEngine(partition.Database, runtimeOptions.Core.QueryExecution, partition.TextProjection));
        RegisterRequestCodec(services, partition, options);
        services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestProgress).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        services.AddSingleton(provider => new ReplicaSiloDiscoveryState(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ILocalSiloDetails>(),
            RuntimeJournalStorePreparation.ReaderEvidence(partition)));
        services.AddSingleton(provider => new ReplicaEnvelopeAuthenticator(provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<ReplicaTransportOptions>>(), provider.GetRequiredService<IOptions<ReplicaReplayLimits>>(),
            logger: provider.GetService<ILogger<ReplicaEnvelopeAuthenticator>>(), canonicalDatabase: partition.Database));
        services.AddSingleton<ReplicaSiloDiscoveryClient>(provider => new ReplicaSiloDiscoveryClient(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            provider.GetRequiredService<ReplicaEnvelopeAuthenticator>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<PeerDiscoveryOptions>>(),
            options.RequestCqrsProbe.DiscoveryCaptureMode == RequestCqrsProbeProtocol.MixedInterface3Capture
                ? provider.GetRequiredService<IReplicaDiscoveryObservationSink>() : null));
        services.AddSingleton<ReplicaGrainServiceClient>();
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, ReplicaTransportLifecycle>();
        RegisterMembershipTable(services, partition, options, startupCancellation);
    }

    private static void RegisterMembershipTable(IServiceCollection services, PartitionHost partition,
        NodeOptions options, CancellationToken startupCancellation)
    {
        if (options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Proxy)
        {
            services.AddSingleton<IMembershipTable>(provider => CreateMembershipProxy(provider, options));
            return;
        }
        services.AddSingleton<IMembershipTable>(provider => new ReplicaMembershipTable(partition.Database, partition.Coordinator,
            partition.Consensus, options.ClusterId, ClusterPrincipalPolicy.InternalPrincipalId, provider.GetRequiredService<TimeProvider>(),
            options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority
                ? provider.GetRequiredService<IOptions<OrleansMembershipOptions>>().Value.MaximumRows
                : ReplicaMembershipProtocol.UnboundedRows,
            provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
            provider.GetRequiredService<IOptions<ReplicaExecutionOptions>>(), startupCancellation));
    }

    private static ReplicaMembershipAuthorityClientTable CreateMembershipProxy(IServiceProvider provider, NodeOptions options)
    {
        var authority = options.MembershipAuthority;
        var callerSecret = Convert.FromBase64String(options.PeerSecret);
        var authoritySecret = Convert.FromBase64String(authority.AuthorityPeerSecret!);
        try
        {
            var local = provider.GetRequiredService<ILocalSiloDetails>();
            var settings = new ReplicaMembershipAuthorityExchangeOptions(options.ClusterId,
                authority.AuthorityPhysicalShardId, authority.AuthorityIncarnation, options.PhysicalShardId,
                options.Incarnation, options.PublicEndpoint, local.SiloAddress.ToParsableString(),
                authority.AuthorityEndpoints.Select(endpoint => new Uri(endpoint)).ToArray(), callerSecret,
                authoritySecret, provider.GetRequiredService<TimeProvider>());
            return new(settings, provider.GetRequiredService<IOptions<OrleansMembershipOptions>>(),
                provider.GetRequiredService<IOptions<ReplicaExecutionOptions>>());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(callerSecret);
            CryptographicOperations.ZeroMemory(authoritySecret);
        }
    }

    private static void RegisterRequestCodec(IServiceCollection services, PartitionHost partition, NodeOptions options)
    {
        if (options.RequestCqrsProbe.Enabled)
        {
            services.AddSingleton(provider => RequestCqrsProbeObserverFactory.Create(
                options.RequestCqrsProbe, provider.GetRequiredService<IOptions<ReplicaConfiguration>>(), options.AllowPrivateNetworkHttp,
                provider.GetRequiredService<ILocalSiloDetails>(), provider.GetRequiredService<IHostApplicationLifetime>(),
                provider.GetRequiredService<IOptions<RequestProbeExecutionOptions>>(), provider.GetRequiredService<TimeProvider>())
                ?? throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidOptions));
            services.AddSingleton<IGrainRequestPhaseObserver>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>());
            if (options.RequestCqrsProbe.DiscoveryCaptureMode == RequestCqrsProbeProtocol.MixedInterface3Capture)
            { services.AddSingleton<IReplicaDiscoveryObservationSink>(provider => provider.GetRequiredService<RequestCqrsProbeObserver>()); }
        }
        services.AddSingleton(provider => new GrainRequestCodec(partition.Database, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<GrainRoutingOptions>>())
        {
            PhaseObserver = provider.GetService<IGrainRequestPhaseObserver>()
        });
    }

    private static void Configure(ISiloBuilder silo, NodeOptions options, ReplicaConfiguration replica, IPAddress address,
        OrleansMembershipOptions membershipOptions, IOptions<RuntimeJournalOptions> journal,
        IOptions<NativeDurableJobOptions> jobs, IOptions<GrainRoutingOptions> routing)
    {
        silo.Configure<ClusterOptions>(cluster =>
        {
            cluster.ClusterId = options.ClusterId;
            cluster.ServiceId = OrleansNodeProtocol.ServiceId;
        });
        silo.ConfigureEndpoints(address, options.SiloPort, OrleansNodeProtocol.GatewayPort);
        silo.Configure<ClusterMembershipOptions>(membership =>
        {
            membership.IAmAliveTablePublishTimeout = membershipOptions.MembershipRefresh;
            membership.TableRefreshTimeout = membershipOptions.MembershipRefresh;
        });
        silo.Configure<SiloMessagingOptions>(messaging => messaging.MaxMessageBodySize = Math.Max(
            checked(replica.MaxAppendBytes + ReplicaTransportProtocol.MaximumMetadataBytes
                + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes),
            checked(routing.Value.MaximumCompletedBytes + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes)));
        silo.AddGrainService<PartitionReplicaGrainService>();
        silo.AddGrainService<RecurringDueGrainService>();
        silo.AddActivityPropagation();
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
        NativeRuntimeJournalRegistration.Register(silo, journal, jobs);
    }
}
