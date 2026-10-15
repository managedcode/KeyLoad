using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class ReplicaReadinessEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet(ServerProtocol.ReadyPath, ReadyAsync);
    }

    private static async Task<IResult> ReadyAsync(PartitionHostApplicationOwner owner, OrleansNode node,
        IOptions<ServerExecutionOptions> options, TimeProvider clock, CancellationToken cancellationToken)
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
            await node.EnsureCompatibleCohortAsync(deadline.Token).ConfigureAwait(false);
            await node.EnsureCatalogAdmissionAsync(deadline.Token).ConfigureAwait(false);
            var partition = owner.Borrow();
            return Results.Ok(new ReadyReply(ServerProtocol.ReadyStatus, partition.Configuration.VoterIds.Length));
        }
        catch (Exception error) when (error is KeyLoadException or OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record ReadyReply(string Status, int Voters);
}
