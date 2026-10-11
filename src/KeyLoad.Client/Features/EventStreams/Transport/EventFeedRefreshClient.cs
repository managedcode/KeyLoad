using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Uses the existing authenticated client for complete event-feed coverage refresh.</summary>
public static class EventFeedRefreshClient
{
    /// <summary>Refreshes authorized source coverage while preserving acknowledged positions.</summary>
    /// <param name="client">The original authenticated client.</param>
    /// <param name="request">The original refresh identity, scope and exact revision, coverage and cursor.</param>
    /// <param name="cancellationToken">Cancels this call without claiming rollback of admitted effects.</param>
    /// <returns>The complete original terminal result or its actual refusal.</returns>
    public static Task<Result<EventFeedControlResult>> RefreshEventFeedAsync(this KeyLoadClient client,
        EventFeedControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != EventFeedControlAction.Refresh)
        { throw new ArgumentException(EventFeedRefreshProtocol.Description, nameof(request)); }
        return client.Send<EventFeedControlResult>(EventFeedRefreshProtocol.Route,
            request, true, request.CommandId, cancellationToken);
    }
}
