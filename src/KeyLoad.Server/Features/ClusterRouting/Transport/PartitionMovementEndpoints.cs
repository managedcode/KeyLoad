namespace KeyLoad.Server.Features.ClusterRouting;

internal static partial class PartitionMovementEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost(PartitionMovementRetireCancellationProtocol.CommandPath, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleRetireCancellationAsync(context, query: false).ConfigureAwait(false);
        });
        app.MapPost(PartitionMovementRetireCancellationProtocol.QueryPath, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleRetireCancellationAsync(context, query: true).ConfigureAwait(false);
        });
        app.MapPost(PartitionMovementProtocol.ReceiverIssuePath, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleReceiverIssueAsync(context, query: false).ConfigureAwait(false);
        });
        app.MapPost(PartitionMovementProtocol.ReceiverIssueProofPath, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleReceiverIssueAsync(context, query: true).ConfigureAwait(false);
        });
        app.MapPost(PartitionMovementProtocol.Path, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleAsync(context).ConfigureAwait(false);
        });
        app.MapPost(PartitionMovementProtocol.TransferDataPath, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleTransferDataAsync(context).ConfigureAwait(false);
        });
        app.MapPost(PartitionMovementProtocol.OutcomePath, static async (HttpContext context, OrleansNode node) =>
        {
            var endpoint = node.Movement?.Endpoint;
            if (endpoint is null)
            { context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; return; }
            await endpoint.HandleOutcomeAsync(context).ConfigureAwait(false);
        });
    }
}
