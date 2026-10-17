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
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<SampleRollupResult>(SampleRollupProtocol.ReadRoute, request, false, null, cancellationToken);
    }
}
