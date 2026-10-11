using ManagedCode.Communication.CQRS;

namespace KeyLoad.Client;

/// <summary>Consumes bounded original event feed offers through the existing SDK transport.</summary>
public static class EventFeedTailClientExtensions
{
    /// <summary>Receives actual persisted offers through native CQRS HTTP streaming without acknowledgement effects.</summary>
    /// <param name="client">The existing authenticated SDK client with centrally configured stream policy.</param>
    /// <param name="request">The original signed-map cursor, read identity and strict delivery limit.</param>
    /// <param name="cancellationToken">The original caller cancellation linked to the owned stream lifetime.</param>
    /// <returns>Complete original offer progress followed by final original evidence or a failed chunk.</returns>
    public static IAsyncEnumerable<CqrsStreamChunk<EventFeedTailProgress, EventFeedControlResult>> TailEventFeedAsync(
        this KeyLoadClient client, EventFeedControlRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.HttpTransport.TailEventFeedAsync(request, cancellationToken);
    }
}
