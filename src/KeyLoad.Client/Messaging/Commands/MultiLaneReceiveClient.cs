using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Independent lane claim composition over the canonical authenticated SDK transport.</summary>
public static class MultiLaneReceiveClientExtensions
{
    /// <summary>Receives ordered independent lane outcomes; retain every original stable lane request for retry.</summary>
    /// <param name="client">The authenticated SDK client.</param>
    /// <param name="request">Bounded ordered stable lane requests and outer correlation identity.</param>
    /// <param name="cancellationToken">Cancels the wait without proving claim rollback.</param>
    /// <returns>Independent committed, rejected, uncertain or unattempted lane outcomes.</returns>
    public static Task<Result<MultiLaneReceiveResult>> ReceiveAcrossLanesAsync(this KeyLoadClient client,
        MultiLaneReceiveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<MultiLaneReceiveResult>(MultiLaneReceiveProtocol.Route, request, true,
            request.RequestId, cancellationToken);
    }
}
