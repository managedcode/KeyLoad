using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Typed rollup access through the existing authenticated SDK transports.</summary>
public static class TimeSeriesRollupClientExtensions
{
    /// <summary>Reads a current explicit bucket; stale snapshots reject.</summary>
    /// <param name="client">Authenticated SDK.</param>
    /// <param name="request">Canonical series and bucket.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    /// <returns>Current revision and nullable bucket.</returns>
    public static Task<Result<SampleRollupResult>> ReadSampleRollupAsync(this KeyLoadClient client,
        ReadSampleRollupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client) /* KLD0041: BCL null validation accepts the client KeyLoadClient reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request ReadSampleRollupRequest reference through its object parameter. */;
        return client.Send<SampleRollupResult, ReadSampleRollupRequest>(SampleRollupProtocol.ReadRoute, request, false, null, cancellationToken);
    }
}
