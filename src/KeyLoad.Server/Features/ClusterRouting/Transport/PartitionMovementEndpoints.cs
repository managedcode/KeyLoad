namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementEndpoints
{
    internal static void Map(WebApplication app)
        => app.MapPost(PartitionMovementProtocol.Path, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleAsync(context).ConfigureAwait(false);
        });
}
