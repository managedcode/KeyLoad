using System.Collections.Immutable;
using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
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

internal static class OrleansSiloConfiguration
{
    internal static IHost Build(PartitionHost partition, NodeOptions options, INodeAdministration administration,
        ILoggerFactory loggerFactory, NativeRequestWorkOwner requestWork, NativeConnectionOwnerIdentity connectionOwner, IPAddress address,
        ServerRuntimeOptions runtimeOptions, TimeProvider clock, INativePartitionMovementCapture? movementCapture, IPartitionMovementDispatcher? movementDispatcher,
        IRemoteDocumentReadRouter? remoteDocuments, IRemoteBlobReadRouter? remoteBlobs, IRemotePartitionQueryRouter? remoteQueries, IRemoteDistributedSearchRouter? distributedSearch,
        IControlledDocumentCommandRouter? controlledDocuments, IGrainPartitionMovementSealedOperationObserver? sealedObserver,
        NativeDiscoveryOmissionOwner? discoveryOmission, IEventVectorParent? eventVectorParent,
        PhysicalOwnerProbeNativeRuntime? physicalProbe, NativeLocalhostClusteringOptions? localhostClustering,
        IRemoteAnnReadRouter? remoteAnnReads, CancellationToken startupCancellation)
    {
        startupCancellation.ThrowIfCancellationRequested();
        var builder = Host.CreateApplicationBuilder();
        OrleansNativePeerServices.RegisterSiloOwners(builder, loggerFactory, connectionOwner,
            discoveryOmission, sealedObserver, localhostClustering);
        if (options.PhysicalGroupTrust.RemoteDocumentReads)
        {
            builder.Services.AddSingleton<IPhysicalRequestPlacement, PhysicalDocumentRequestPlacement>();
            if (remoteDocuments is not null)
            { builder.Services.AddSingleton(remoteDocuments); }
            if (remoteBlobs is not null)
            { builder.Services.AddSingleton(remoteBlobs); }
            if (remoteQueries is not null)
            { builder.Services.AddSingleton(remoteQueries); }
            if (distributedSearch is not null)
            { builder.Services.AddSingleton(distributedSearch); }
        }
        if (controlledDocuments is not null)
        { builder.Services.AddSingleton(controlledDocuments); }
        if (movementCapture is not null)
        { builder.Services.AddSingleton(movementCapture); }
        if (movementCapture is PartitionMovementSourceOwner sourceOwner)
        { builder.Services.AddSingleton(sourceOwner.TransferReads); }
        if (movementDispatcher is not null)
        { builder.Services.AddSingleton(movementDispatcher); }
        if (movementDispatcher is PartitionMovementRuntime { NativeReceiver: { } nativeMovement })
        { builder.Services.AddSingleton(nativeMovement); }
        OrleansNativePeerServices.RegisterPhysicalProbe(builder.Services, physicalProbe);
        builder.Services.AddSingleton(provider => new PartitionMovementGrainServiceClient(provider, partition,
            runtimeOptions.Node, runtimeOptions.Membership, runtimeOptions.GrainRouting,
            provider.GetRequiredService<ISiloStatusOracle>(), provider.GetRequiredService<ReplicaSiloDiscoveryState>(), clock));
        builder.Services.AddSingleton(provider => new global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRuntime(
            partition, runtimeOptions.Node, runtimeOptions.Membership, runtimeOptions.GrainRouting,
            provider.GetRequiredService<ISiloStatusOracle>(), provider.GetRequiredService<ReplicaSiloDiscoveryState>(),
            clock, provider.GetRequiredService<ILogger<global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRuntime>>()));
        builder.Services.AddSingleton(provider => new global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeClient(
            provider, partition, runtimeOptions.Node, runtimeOptions.Membership, runtimeOptions.GrainRouting,
            provider.GetRequiredService<PartitionMovementGrainServiceClient>(), provider.GetRequiredService<ReplicaSiloDiscoveryState>(), clock));
        if (movementDispatcher is IPartitionMovementParent movementParent)
        { builder.Services.AddSingleton(movementParent); }
        if (eventVectorParent is not null)
        { builder.Services.AddSingleton(eventVectorParent); }
        runtimeOptions.RegisterBorrowed(builder.Services);
        RegisterBorrowedServices(builder.Services, partition, administration, options, requestWork, runtimeOptions, clock);
        if (remoteAnnReads is not null)
        { builder.Services.AddSingleton(remoteAnnReads); }
        builder.UseOrleans(silo => Configure(silo, options, partition.Configuration, address, runtimeOptions.Membership.Value,
            runtimeOptions.Core.RuntimeJournal, runtimeOptions.DurableJobs, runtimeOptions.GrainRouting, runtimeOptions.Core.DatabaseLimits, localhostClustering));
        return builder.Build();
    }

    private static void RegisterBorrowedServices(IServiceCollection services, PartitionHost partition,
        INodeAdministration administration, NodeOptions options, NativeRequestWorkOwner requestWork,
        ServerRuntimeOptions runtimeOptions, TimeProvider clock)
    {
        var peers = runtimeOptions.Peer.Value;
        peers.Validate(partition.Configuration);
        var expectedOwner = new PhysicalShardRecord(options.PhysicalShardId, partition.Configuration.Incarnation,
            ImmutableArray.CreateRange(partition.Configuration.VoterIds),
            PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch);
        services.AddSingleton(expectedOwner);
        services.AddSingleton(partition.Database);
        services.AddSingleton<ICacheMemoryBudget>(partition.CacheMemory);
        services.AddSingleton<INativeAnnMaintenance>(partition.AnnMaintenance);
        services.AddSingleton<INativeTextMaintenance>(partition.TextMaintenance);
        services.AddSingleton<INativeOnlineTextMaintenance>(partition.OnlineTextMaintenance);
        services.AddSingleton<ICommitCoordinator>(partition.Coordinator);
        services.AddSingleton<IReplicaEndpoint>(partition.Consensus);
        services.AddSingleton(partition.Consensus);
        services.AddSingleton(clock);
        services.AddSingleton(requestWork);
        services.AddSingleton(administration);
        services.AddSingleton<QueryEngine>();
        services.AddSingleton(provider => new SearchEngine(partition.Database, runtimeOptions.Core.QueryExecution,
            partition.TextProjection, partition.AnnMaintenance, provider.GetService<IQueryReadCutObserver>()));
        global::KeyLoad.Server.Features.BlobStorage.NativeBlobWireProbeRegistration.Register(services, partition, options);
        RegisterRequestCodec(services, partition, options);
        services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestProgress).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        services.AddSingleton(provider => new ReplicaSiloDiscoveryState(
            provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ILocalSiloDetails>(),
            RuntimeJournalStorePreparation.CurrentCapabilityEvidence(partition)));
        services.AddSingleton(provider => new ReplicaEnvelopeAuthenticator(provider.GetRequiredService<IOptions<ReplicaConfiguration>>(),
            provider.GetRequiredService<IOptions<ReplicaPeerOptions>>(),
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<ReplicaTransportOptions>>(), provider.GetRequiredService<IOptions<ReplicaReplayLimits>>(),
            logger: provider.GetService<ILogger<ReplicaEnvelopeAuthenticator>>(), canonicalDatabase: partition.Database));
        OrleansNativePeerServices.RegisterReplicaDiscovery(services);
        services.AddSingleton<ReplicaGrainServiceClient>();
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, ReplicaTransportLifecycle>();
    }

    private static void RegisterRequestCodec(IServiceCollection services, PartitionHost partition, NodeOptions options)
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

    private static void Configure(ISiloBuilder silo, NodeOptions options, ReplicaConfiguration replica, IPAddress address,
        OrleansMembershipOptions membershipOptions, IOptions<RuntimeJournalOptions> journal,
        IOptions<NativeDurableJobOptions> jobs, IOptions<GrainRoutingOptions> routing, IOptions<DatabaseLimits> databaseLimits,
        NativeLocalhostClusteringOptions? localhostClustering)
    {
        silo.Configure<ClusterOptions>(cluster =>
        {
            cluster.ClusterId = options.ClusterId;
            cluster.ServiceId = OrleansNodeProtocol.ServiceId;
        });
        OrleansNativeClustering.Configure(silo, options, replica, address, localhostClustering);
        silo.Configure<ClusterMembershipOptions>(membership =>
        {
            membership.IAmAliveTablePublishTimeout = membershipOptions.MembershipRefresh;
            membership.TableRefreshTimeout = membershipOptions.MembershipRefresh;
        });
        silo.Configure<SiloMessagingOptions>(messaging => messaging.MaxMessageBodySize = Math.Max(
            checked(databaseLimits.Value.MaxBatchBytes + ReplicaTransportProtocol.MaximumMetadataBytes
                + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes),
            Math.Max(checked(replica.MaxAppendBytes + ReplicaTransportProtocol.MaximumMetadataBytes
                + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes),
                checked(routing.Value.MaximumCompletedBytes + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes))));
        silo.AddGrainService<PartitionReplicaGrainService>();
        silo.AddGrainService<PartitionMovementGrainService>();
        silo.AddGrainService<global::KeyLoad.Server.Features.DocumentStorage.RemoteModelGrainService>();
        if (options.RequestCqrsProbe.Enabled)
        { silo.AddGrainService<RequestCqrsProbeGrainService>(); }
        silo.AddGrainService<RecurringDueGrainService>();
        silo.AddGrainService<SampleChunkGrainService>();
        silo.AddGrainService<AnnProjectionGrainService>();
        silo.AddActivityPropagation();
        // ADR-036: owner explicitly requires these two native experimental services.
#pragma warning disable ORLEANSEXP003
        silo.AddDistributedGrainDirectory();
#pragma warning restore ORLEANSEXP003
#pragma warning disable ORLEANSEXP001
        silo.AddActivationRepartitioner();
#pragma warning restore ORLEANSEXP001
        ConnectionGrainGraphRegistration.Register(silo, options.RequestCqrsProbe.Enabled);
        NativeRuntimeJournalRegistration.Register(silo, journal, jobs);
    }
}
