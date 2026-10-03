using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server;

internal static class ReplicaDiscoveryEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet(ReplicaProtocol.DiscoveryPath, DiscoveryAsync);
        app.MapGet(ServerProtocol.ReadyPath, ReadyAsync);
    }

    private static async Task<IResult> DiscoveryAsync(HttpContext context, PeerSecurity security, OrleansNode node)
    {
        if (context.Request.ContentLength is > 0 || context.Request.QueryString.HasValue
            || !await security.ValidateAsync(context.Request, context.RequestAborted).ConfigureAwait(false))
        {
            return Results.Unauthorized();
        }
        var state = node.Discovery?.Read();
        var authentication = node.Authentication;
        if (state is not { TransportReady: true } || authentication is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (!Guid.TryParseExact(context.Request.Headers[ServerProtocol.NonceHeader], OrleansNodeProtocol.GuidFormat, out var nonce))
        {
            return Results.Unauthorized();
        }
        var payload = NativeSerialization.Serialize(state);
        context.Response.Headers[ReplicaTransportProtocol.DiscoverySignatureHeader] = authentication.SignDiscovery(payload, nonce);
        return Results.Bytes(payload, ServerProtocol.BinaryContentType);
    }

    private static async Task<IResult> ReadyAsync(PartitionHost partition, OrleansNode node, CancellationToken cancellationToken)
    {
        if (node.Grains is null)
        { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ServerProtocol.ReadyTimeout);
        try
        {
            await partition.Coordinator.ReadBarrierAsync(deadline.Token).ConfigureAwait(false);
            return Results.Ok(new ReadyReply(ServerProtocol.ReadyStatus, partition.Configuration.VoterIds.Length));
        }
        catch (Exception error) when (error is KeyLoadException or OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record ReadyReply(string Status, int Voters);
}
