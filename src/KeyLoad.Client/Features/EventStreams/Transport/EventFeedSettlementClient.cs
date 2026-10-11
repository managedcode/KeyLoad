using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Provides complete event-feed settlement through the existing authenticated SDK transport.</summary>
public static class EventFeedSettlementClient
{
    /// <summary>Acknowledges the exact original offered vector.</summary>
    /// <param name="client">The existing authenticated client.</param>
    /// <param name="request">The original acknowledgement identity, cursor and offer token.</param>
    /// <param name="cancellationToken">Cancels this call without claiming rollback.</param>
    /// <returns>The full original terminal result or its actual failure.</returns>
    public static Task<Result<EventFeedControlResult>> AcknowledgeEventFeedAsync(this KeyLoadClient client,
        EventFeedControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != EventFeedControlAction.Acknowledge)
        { throw new ArgumentException(EventFeedSettlementProtocol.AcknowledgeDescription, nameof(request)); }
        return client.Send<EventFeedControlResult>(EventFeedSettlementProtocol.AcknowledgeRoute,
            request, true, request.CommandId, cancellationToken);
    }

    /// <summary>Releases the original authorized event-feed ownership.</summary>
    /// <param name="client">The existing authenticated client.</param>
    /// <param name="request">The immutable original release identity and scope.</param>
    /// <param name="cancellationToken">Cancels this call without claiming rollback.</param>
    /// <returns>The full original terminal result or its actual retained disposition.</returns>
    public static Task<Result<EventFeedControlResult>> ReleaseEventFeedAsync(this KeyLoadClient client,
        EventFeedControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != EventFeedControlAction.Release)
        { throw new ArgumentException(EventFeedSettlementProtocol.ReleaseDescription, nameof(request)); }
        return client.Send<EventFeedControlResult>(EventFeedSettlementProtocol.ReleaseRoute,
            request, true, request.CommandId, cancellationToken);
    }
}
