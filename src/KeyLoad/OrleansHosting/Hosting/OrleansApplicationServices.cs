using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class OrleansApplicationServices
{
    internal static void RegisterSiloOwners(WebApplicationBuilder builder,
        LocalhostClusteringOptions? localhostClustering)
    {
        var services = builder.Services;
        if (localhostClustering is null)
        {
            OrleansClustering.RegisterHostingOptions(builder);
        }
        services.AddSingleton(provider => provider.GetRequiredService<OrleansNode>().ConnectionOwner);
        services.AddSingleton<Func<RequestWorkOwner>>(provider =>
        {
            var borrowed = provider.GetRequiredService<OrleansNode>().RequestWork;
            return () => borrowed;
        });
    }
}
