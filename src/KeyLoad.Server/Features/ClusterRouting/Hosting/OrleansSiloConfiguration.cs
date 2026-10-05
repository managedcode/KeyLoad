using System.Net;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Replication;
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
        services.AddSingleton<GrainRequestCodec>();
        services.AddSerializer(serialization => serialization
            .AddAssembly(typeof(GrainRequestProgress).Assembly)
            .AddAssembly(typeof(CqrsStreamChunkSurrogateConverter<GrainRequestProgress, GrainOperationReply>).Assembly)
            .AddAssembly(typeof(ClaimsPrincipalSurrogateConverter).Assembly));
        services.AddSingleton<ReplicaSiloDiscoveryState>();
        services.AddSingleton(provider => new ReplicaEnvelopeAuthenticator(partition.Configuration, peers,
            provider.GetRequiredService<ReplicaSiloDiscoveryState>(), TimeProvider.System,
            logger: provider.GetService<ILogger<ReplicaEnvelopeAuthenticator>>(), canonicalDatabase: partition.Database));
        services.AddSingleton<ReplicaSiloDiscoveryClient>();
        services.AddSingleton<ReplicaGrainServiceClient>();
        services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>, ReplicaTransportLifecycle>();
        services.AddSingleton<IMembershipTable>(new ReplicaMembershipTable(partition.Database, partition.Coordinator,
            partition.Consensus, options.ClusterId, ClusterPrincipalPolicy.InternalPrincipalId, TimeProvider.System,
            startupCancellation));
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
