using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Explicit remote graph operations over the existing authenticated SDK transport.</summary>
public static class RemoteGraphClient
{
    /// <summary>Upserts a remote graph operation with its original stable command identity.</summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="request">The exact original typed request; retries preserve its full payload.</param>
    /// <param name="cancellationToken">Cancellation for the original transport operation.</param>
    /// <returns>The actual separate source/target/completion receipts or typed uncertainty/refusal.</returns>
    public static Task<Result<RemoteGraphEdgeResultV1>> UpsertRemoteGraphEdgeAsync(this KeyLoadClient client,
        UpsertRemoteGraphEdgeRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<RemoteGraphEdgeResultV1>(RemoteGraphPublicProtocol.UpsertRoute, request,
            true, request.CommandId, cancellationToken);
    }

    /// <summary>Deletes a remote graph operation with its original stable command identity.</summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="request">The exact original typed request; retries preserve its full payload.</param>
    /// <param name="cancellationToken">Cancellation for the original transport operation.</param>
    /// <returns>The actual separate source/target/completion receipts or typed uncertainty/refusal.</returns>
    public static Task<Result<RemoteGraphEdgeResultV1>> DeleteRemoteGraphEdgeAsync(this KeyLoadClient client,
        DeleteRemoteGraphEdgeRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<RemoteGraphEdgeResultV1>(RemoteGraphPublicProtocol.DeleteRoute, request,
            true, request.CommandId, cancellationToken);
    }

    /// <summary>Delivers a remote graph operation with its original stable command identity.</summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="request">The exact original typed request; retries preserve its full payload.</param>
    /// <param name="cancellationToken">Cancellation for the original transport operation.</param>
    /// <returns>The actual separate source/target/completion receipts or typed uncertainty/refusal.</returns>
    public static Task<Result<RemoteGraphEdgeResultV1>> DeliverRemoteGraphEdgeAsync(this KeyLoadClient client,
        DeliverRemoteGraphEdgeRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<RemoteGraphEdgeResultV1>(RemoteGraphPublicProtocol.DeliverRoute, request,
            true, request.CommandId, cancellationToken);
    }

    /// <summary>Reads one eventual incoming projection with separately declared owner cuts.</summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="request">The original target, graph, version and result bound.</param>
    /// <param name="cancellationToken">Cancellation for the original transport operation.</param>
    /// <returns>The actual bounded projection page or typed refusal; no global snapshot is implied.</returns>
    public static Task<Result<RemoteIncomingGraphEdgesPageV1>> RemoteIncomingGraphEdgesAsync(this KeyLoadClient client,
        ReadRemoteIncomingGraphEdgesRequestV1 request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<RemoteIncomingGraphEdgesPageV1>(RemoteGraphPublicProtocol.IncomingRoute, request,
            false, null, cancellationToken);
    }
}
