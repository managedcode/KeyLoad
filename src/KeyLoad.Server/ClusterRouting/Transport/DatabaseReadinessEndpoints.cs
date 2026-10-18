using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class DatabaseReadinessEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet(ServerProtocol.ReadyPath, ReadyAsync);
    }

    private static async Task<IResult> ReadyAsync(PartitionHostApplicationOwner owner, OrleansNode node,
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
            await owner.Borrow().Coordinator.ReadBarrierAsync(deadline.Token).ConfigureAwait(false);
            var partition = owner.Borrow();
            return Results.Ok(new ReadyReply(ServerProtocol.ReadyStatus, nodeOptions.Value.ReplicaCount));
        }
        catch (Exception error) when (error is KeyLoadException or OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record ReadyReply(string Status, int ReplicaCount);
}
