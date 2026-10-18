using System.Net;
using KeyLoad.Clustering;
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
        OrleansApplicationServices.RegisterSiloOwners(builder, localhostClustering);
        global::KeyLoad.Server.Features.ResourceExecution.NativeDropTableRegistration.Register(builder.Services);
        OrleansPartitionServiceRegistration.Register(builder.Services, options);
        OrleansRequestServiceRegistration.Register(builder.Services);
        builder.UseOrleans(silo => Configure(silo, options, address, localhostClustering));
        OrleansNativeHostedServiceRegistration.Guard(builder.Services);
    }

    private static void Configure(ISiloBuilder silo, NodeOptions options,
        IPAddress address, NativeLocalhostClusteringOptions? localhostClustering)
    {
        silo.Configure<ClusterOptions>(cluster =>
        {
            cluster.ClusterId = options.ClusterId;
            cluster.ServiceId = OrleansNodeProtocol.ServiceId;
        });
        OrleansNativeClustering.Configure(silo, options, address, localhostClustering);
        ConfigureMessaging(silo.Services);
        PartitionStorageRegistration.Register(silo);
        silo.Services.AddSingleton<OrleansNodeLifecycle>();
        silo.Services.AddSingleton<ILifecycleParticipant<ISiloLifecycle>>(provider => provider.GetRequiredService<OrleansNodeLifecycle>());
        silo.AddActivityPropagation();
#pragma warning disable ORLEANSEXP003
        silo.AddDistributedGrainDirectory();
#pragma warning restore ORLEANSEXP003
#pragma warning disable ORLEANSEXP001
        silo.AddActivationRepartitioner();
#pragma warning restore ORLEANSEXP001
        ConnectionGrainGraphRegistration.Register(silo);
        NativeRuntimeJournalRegistration.Register(silo);
    }

    private static void ConfigureMessaging(IServiceCollection services)
        => services.AddOptions<SiloMessagingOptions>().Configure<IOptions<DatabaseLimits>,
            IOptions<GrainRoutingOptions>>((messaging, limits, routing) => messaging.MaxMessageBodySize = Math.Max(
                limits.Value.MaxBatchBytes, routing.Value.MaximumCompletedBytes));
}
