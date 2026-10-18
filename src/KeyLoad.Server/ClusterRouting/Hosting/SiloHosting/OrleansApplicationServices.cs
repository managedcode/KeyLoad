using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class OrleansApplicationServices
{
    internal static void RegisterSiloOwners(WebApplicationBuilder builder,
        NativeLocalhostClusteringOptions? localhostClustering)
    {
        var services = builder.Services;
        if (localhostClustering is null)
        {
            OrleansNativeClustering.RegisterHostingOptions(builder);
        }
        services.AddSingleton(provider => provider.GetRequiredService<OrleansNode>().ConnectionOwner);
        services.AddSingleton<Func<NativeRequestWorkOwner>>(provider =>
        {
            var borrowed = provider.GetRequiredService<OrleansNode>().RequestWork;
            return () => borrowed;
        });
    }
}
