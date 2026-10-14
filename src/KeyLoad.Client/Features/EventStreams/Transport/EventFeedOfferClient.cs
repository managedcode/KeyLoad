using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Provides the complete Offer parent through the existing authenticated SDK transport.</summary>
public static class EventFeedOfferClient
{
    /// <summary>Offers complete bounded pages from an authorized event feed.</summary>
    /// <param name="client">The existing authenticated client.</param>
    /// <param name="request">The immutable original Offer identity, cursor and scope.</param>
    /// <param name="cancellationToken">Cancels the original call without claiming rollback.</param>
    /// <returns>The complete original terminal result or its actual failure.</returns>
    public static Task<Result<EventFeedControlResult>> OfferEventFeedAsync(this KeyLoadClient client,
        EventFeedControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != EventFeedControlAction.Offer)
        {
            throw new ArgumentException(EventFeedOfferProtocol.Description, nameof(request));
        }
        return client.Send<EventFeedControlResult>(EventFeedOfferProtocol.Route, request, true, request.CommandId, cancellationToken);
    }
}
