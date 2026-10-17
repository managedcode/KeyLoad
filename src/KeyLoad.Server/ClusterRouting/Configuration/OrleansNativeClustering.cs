using System.Net;
using KeyLoad.Clustering;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace KeyLoad.Server;

internal static class OrleansNativeClustering
{
    [ConfigurationBinding]
    internal static void RegisterHostingOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<OrleansHostingOptions>()
            .Configure(hosting => hosting.ConnectionString = global::Microsoft.Extensions.Configuration.ConfigurationExtensions
                .GetConnectionString(builder.Configuration, OrleansHostingOptions.ConnectionName) ?? string.Empty)
            .Validate(hosting => hosting.IsValid(), OrleansHostingOptions.ValidationMessage).ValidateOnStart();
    }

    internal static void Configure(ISiloBuilder silo, NodeOptions options, ReplicaConfiguration replica,
        IPAddress address, NativeLocalhostClusteringOptions? localhostClustering)
        => ConfigureMembership(silo, replica.LocalId, options.ClusterId, options.SiloPort, address, localhostClustering);

    internal static void ConfigureMembership(ISiloBuilder silo, string siloName, string clusterId, int siloPort,
        IPAddress address, NativeLocalhostClusteringOptions? localhostClustering)
    {
        silo.Configure<SiloOptions>(identity => identity.SiloName = siloName);
        if (localhostClustering is null)
        {
            silo.UseAzureStorageClustering((OptionsBuilder<global::Orleans.Clustering.AzureStorage.AzureStorageClusteringOptions> azure) =>
                azure.Configure<IOptions<OrleansHostingOptions>>((provider, hosting) =>
                    provider.TableServiceClient = new global::Azure.Data.Tables.TableServiceClient(hosting.Value.ConnectionString)));
        }
        else
        {
            silo.UseLocalhostClustering(siloPort: siloPort,
                gatewayPort: OrleansNodeProtocol.GatewayPort,
                primarySiloEndpoint: localhostClustering.PrimarySiloEndpoint,
                serviceId: OrleansNodeProtocol.ServiceId, clusterId: clusterId);
        }
        silo.ConfigureEndpoints(address, siloPort, OrleansNodeProtocol.GatewayPort);
    }
}
