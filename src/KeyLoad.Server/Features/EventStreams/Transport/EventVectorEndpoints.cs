namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(EventVectorRemoteProtocol.Path, static (HttpContext context, OrleansNode node) =>
        {
            if (node.RemoteDocuments?.EventVectors?.Endpoint is not { } receiver)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.CompletedTask;
            }
            return receiver.HandleAsync(context);
        });
    }
}
