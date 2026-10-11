using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Provides the complete Open parent through the existing authenticated SDK transport.</summary>
public static class EventFeedOpenClient
{
    /// <summary>Opens one bounded authorized event feed.</summary>
    /// <param name="client">The existing authenticated client.</param>
    /// <param name="request">The immutable original Open identity and scope.</param>
    /// <param name="cancellationToken">Cancels the original call without claiming rollback.</param>
    /// <returns>The complete original terminal result or its actual failure.</returns>
    public static Task<Result<EventFeedControlResult>> OpenEventFeedAsync(this KeyLoadClient client,
        EventFeedControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action != EventFeedControlAction.Open)
        { throw new ArgumentException(EventFeedOpenProtocol.Description, nameof(request)); }
        return client.Send<EventFeedControlResult>(EventFeedOpenProtocol.Route, request, true, request.CommandId, cancellationToken);
    }
}
