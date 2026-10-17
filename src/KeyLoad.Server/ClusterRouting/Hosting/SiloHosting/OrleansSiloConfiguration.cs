using System.Net;
using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace KeyLoad.Server;

internal static class OrleansSiloConfiguration
{
    internal static void Register(WebApplicationBuilder builder, IOptions<NodeOptions> nodeOptions, IPAddress address,
        NativeLocalhostClusteringOptions? localhostClustering = null)
    {
        var options = nodeOptions.Value;
        options.Validate();
        var replica = new ReplicaConfigurationFactory(nodeOptions).Create(Options.DefaultName);
        OrleansNativePeerServices.RegisterSiloOwners(builder, options, localhostClustering);
        OrleansMovementServiceRegistration.Register(builder.Services, options);
        OrleansPartitionServiceRegistration.Register(builder.Services, options);
        OrleansPartitionRuntimeRegistration.Register(builder.Services);
        OrleansRequestServiceRegistration.Register(builder.Services, options);
        builder.UseOrleans(silo => Configure(silo, options, replica, address, localhostClustering));
        OrleansNativeHostedServiceRegistration.Guard(builder.Services);
    }

    private static void Configure(ISiloBuilder silo, NodeOptions options, ReplicaConfiguration replica,
        IPAddress address, NativeLocalhostClusteringOptions? localhostClustering)
    {
        silo.Configure<ClusterOptions>(cluster =>
        {
            cluster.ClusterId = options.ClusterId;
            cluster.ServiceId = OrleansNodeProtocol.ServiceId;
        });
        OrleansNativeClustering.Configure(silo, options, replica, address, localhostClustering);
        ConfigureMessaging(silo.Services);
        PartitionStorageRegistration.Register(silo);
        silo.AddGrainService<PartitionReplicaGrainService>();
        silo.AddGrainService<PartitionMovementGrainService>();
        silo.AddGrainService<global::KeyLoad.Server.Features.DocumentStorage.RemoteModelGrainService>();
        silo.AddGrainService<global::KeyLoad.Server.Features.GraphTraversal.RemoteGraphGrainService>();
        if (options.RequestCqrsProbe.Enabled)
        {
            silo.AddGrainService<RequestCqrsProbeGrainService>();
        }
        silo.AddGrainService<RecurringDueGrainService>();
        silo.AddGrainService<SampleChunkGrainService>();
        silo.AddGrainService<AnnProjectionGrainService>();
        silo.Services.AddSingleton<OrleansNodeLifecycle>();
        silo.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider => provider.GetRequiredService<OrleansNodeLifecycle>());
        silo.AddActivityPropagation();
#pragma warning disable ORLEANSEXP003
        silo.AddDistributedGrainDirectory();
#pragma warning restore ORLEANSEXP003
#pragma warning disable ORLEANSEXP001
        silo.AddActivationRepartitioner();
#pragma warning restore ORLEANSEXP001
        ConnectionGrainGraphRegistration.Register(silo, options.RequestCqrsProbe.Enabled);
        NativeRuntimeJournalRegistration.Register(silo);
        EventFeedNativeProviderRegistration.Register(silo);
    }

    private static void ConfigureMessaging(IServiceCollection services)
        => services.AddOptions<SiloMessagingOptions>().Configure<IOptions<DatabaseLimits>, IOptions<ReplicaConfiguration>,
            IOptions<GrainRoutingOptions>>((messaging, limits, replica, routing) => messaging.MaxMessageBodySize = Math.Max(
                checked(limits.Value.MaxBatchBytes + ReplicaTransportProtocol.MaximumMetadataBytes
                    + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes),
                Math.Max(checked(replica.Value.MaxAppendBytes + ReplicaTransportProtocol.MaximumMetadataBytes
                    + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes),
                    checked(routing.Value.MaximumCompletedBytes + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes))));
}
