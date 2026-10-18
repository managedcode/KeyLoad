using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class DatabaseReadinessEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet(ServerProtocol.ReadyPath, ReadyAsync);
    }

    private static async Task<IResult> ReadyAsync(OrleansNode node,
        IOptions<ServerExecutionOptions> options, IOptions<NodeOptions> nodeOptions, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!node.DatabaseReady)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        var readyTimeout = options.Value.ReadyTimeout;
        using var timeout = new CancellationTokenSource(readyTimeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var factory = node.Grains ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
            var incarnation = nodeOptions.Value.Incarnation;
            var cut = await factory.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(incarnation))
                .ReadCutAsync(deadline.Token).ConfigureAwait(false);
            if (cut.Incarnation != incarnation)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            return Results.Ok(new ReadyReply(ServerProtocol.ReadyStatus, nodeOptions.Value.ReplicaCount));
        }
        catch (Exception error) when (error is KeyLoadException or OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record ReadyReply(string Status, int ReplicaCount);
}
