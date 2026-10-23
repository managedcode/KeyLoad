using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Reads a bounded page from one event stream.</summary>
    /// <param name="request">Stream identity, cursor and page limit.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed stream page.</returns>
    public Task<Result<StreamPage>> ReadStreamAsync(ReadStreamRequest request, CancellationToken cancellationToken = default)
        => Send<StreamPage>(ApiRoutes.StreamsRead, request, false, null, cancellationToken);
    /// <summary>Reads a bounded page from one event source.</summary>
    /// <param name="request">Source identity, cursor and page limit.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP operation.</param>
    /// <returns>The typed event-source page.</returns>
    public Task<Result<EventSourcePage>> ReadEventSourceAsync(ReadEventSourceRequest request, CancellationToken cancellationToken = default)
        => Send<EventSourcePage>(ApiRoutes.EventsRead, request, false, null, cancellationToken);
}
