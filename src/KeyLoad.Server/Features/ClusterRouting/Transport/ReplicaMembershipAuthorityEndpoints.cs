using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipAuthorityEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(ReplicaMembershipAuthorityProtocol.Path,
            static (HttpContext context, ReplicaMembershipAuthorityEndpoint endpoint) => endpoint.HandleAsync(context));
    }
}
