using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class OrleansApplicationServices
{
    internal static void RegisterSiloOwners(WebApplicationBuilder builder)
    {
        var services = builder.Services;
        OrleansClustering.RegisterHostingOptions(builder);
        services.AddSingleton(provider => provider.GetRequiredService<OrleansNode>().ConnectionOwner);
        services.AddSingleton<Func<RequestWorkOwner>>(provider =>
        {
            var borrowed = provider.GetRequiredService<OrleansNode>().RequestWork;
            return () => borrowed;
        });
    }
}
