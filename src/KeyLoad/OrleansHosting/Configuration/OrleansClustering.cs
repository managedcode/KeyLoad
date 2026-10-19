using System.Net;
using KeyLoad.Clustering;
using ManagedCode.Orleans.RapidMembership;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace KeyLoad.Server;

internal static class OrleansClustering
{
    [ConfigurationBinding]
    internal static void RegisterHostingOptions(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<OrleansHostingOptions>()
            .Bind(builder.Configuration.GetSection(OrleansHostingOptions.SectionName))
            .Validate(hosting => hosting.IsValid(), OrleansHostingOptions.ValidationMessage).ValidateOnStart();
        builder.Services.AddOptions<RapidMembershipOptions>()
            .Configure<IOptions<OrleansHostingOptions>>((membership, hosting) =>
            {
                membership.SharedSecret = hosting.Value.SharedSecret;
                membership.ListenPort = hosting.Value.ListenPort;
                membership.DiscoveryNetworks = hosting.Value.DiscoveryNetworks;
            });
    }

    internal static void Configure(ISiloBuilder silo, NodeOptions options, IPAddress address)
        => ConfigureMembership(silo, options.SiloName, options.SiloPort, address);

    internal static void ConfigureMembership(ISiloBuilder silo, string siloName, int siloPort, IPAddress address)
    {
        silo.Configure<SiloOptions>(identity => identity.SiloName = siloName);
        silo.UseRapidMembership(static _ => { });
        silo.ConfigureEndpoints(address, siloPort, OrleansNodeProtocol.GatewayPort);
    }
}
