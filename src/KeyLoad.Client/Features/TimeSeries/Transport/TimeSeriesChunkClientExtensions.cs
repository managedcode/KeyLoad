using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Reads the current authorized native chunk window through the existing signed SDK transport.</summary>
public static class TimeSeriesChunkClientExtensions
{
    /// <summary>Reads a complete bounded window at the current native cut.</summary>
    /// <param name="client">Actual signed database client.</param>
    /// <param name="request">Enrolled window, scalar tag predicate and strict output cap.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    /// <returns>Complete generation metadata and authorized sample rows; failures expose no partial result.</returns>
    public static Task<Result<SampleChunkWindowResult>> ReadSampleChunkWindowAsync(this KeyLoadClient client,
        ReadSampleChunkWindowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        return client.Send<SampleChunkWindowResult>(SampleChunkProtocol.ReadRoute, request, false, null,
            cancellationToken);
    }
}
