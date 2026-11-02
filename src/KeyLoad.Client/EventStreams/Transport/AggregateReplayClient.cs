using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Provides the typed aggregate replay operation on the shared authenticated SDK transport.</summary>
public static class AggregateReplayClient
{
    /// <summary>Reads one authorized, complete aggregate replay slice through the public API.</summary>
    /// <param name="client">The existing authenticated database client.</param>
    /// <param name="request">The stream generation, exact worker versions and complete-tail limit.</param>
    /// <param name="cancellationToken">Token that cancels the HTTP read.</param>
    /// <returns>The snapshot and complete same-cut tail, or a classified transport/server problem.</returns>
    public static Task<Result<AggregateReplayPage>> ReadAggregateReplayAsync(
        this KeyLoadClient client,
        ReadAggregateReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request ReadAggregateReplayRequest reference through its object parameter. */;
        return client.Send<AggregateReplayPage, ReadAggregateReplayRequest>(global::KeyLoad.AggregateReplayProtocol.Route, request, false, null, cancellationToken);
    }
}
