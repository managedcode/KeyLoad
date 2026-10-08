namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteDocumentEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(RemoteDocumentProtocol.Path, static (HttpContext context, OrleansNode node) =>
        {
            if (node.RemoteDocuments?.Endpoint is not { } receiver)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return Task.CompletedTask; }
            return receiver.HandleAsync(context);
        });
    }
}
