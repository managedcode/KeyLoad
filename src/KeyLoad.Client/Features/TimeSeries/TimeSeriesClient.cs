using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Reads the exclusive retention floor and physical purge progress.</summary>
    /// <param name="request">Partition and series identity under current persisted rights.</param>
    /// <param name="cancellationToken">Cancellation throughout the HTTP operation.</param>
    /// <returns>Persisted UTC floor, cumulative deletion count and remaining-page status.</returns>
    public Task<Result<SampleRetentionStatus>> ReadSampleRetentionAsync(ReadSampleRetentionRequest request,
        CancellationToken cancellationToken = default)
        => Send<SampleRetentionStatus>(TimeSeriesReadProtocol.RetentionRoute, request, false, null, cancellationToken);

    /// <summary>Reads the latest projected sample at an optional inclusive timestamp.</summary>
    /// <param name="request">Series identity and optional inclusive timestamp cut.</param>
    /// <param name="cancellationToken">Cancellation throughout the HTTP operation.</param>
    /// <returns>The latest sample wrapper, containing null when the series is absent.</returns>
    public Task<Result<LatestSampleResult>> ReadLatestSampleAsync(ReadLatestSampleRequest request,
        CancellationToken cancellationToken = default)
        => Send<LatestSampleResult>(TimeSeriesReadProtocol.LatestRoute, request, false, null, cancellationToken);

    /// <summary>Reads complete finite raw statistics over a half-open UTC range.</summary>
    /// <param name="request">Series range and strict sample cap.</param>
    /// <param name="cancellationToken">Cancellation throughout the HTTP operation.</param>
    /// <returns>Complete statistics; overflowing the sample cap rejects the operation.</returns>
    public Task<Result<SampleAggregate>> AggregateSamplesAsync(AggregateSamplesRequest request,
        CancellationToken cancellationToken = default)
        => Send<SampleAggregate>(TimeSeriesReadProtocol.AggregateRoute, request, false, null, cancellationToken);

    /// <summary>Reads dense fixed-width UTC windows anchored at the requested beginning.</summary>
    /// <param name="request">Series range, positive width and sample/window caps.</param>
    /// <param name="cancellationToken">Cancellation throughout the HTTP operation.</param>
    /// <returns>The complete immutable window result after server input/output limits.</returns>
    public Task<Result<SampleAggregateWindowsResult>> AggregateSampleWindowsAsync(AggregateSampleWindowsRequest request,
        CancellationToken cancellationToken = default)
        => Send<SampleAggregateWindowsResult>(TimeSeriesReadProtocol.WindowsRoute, request, false, null, cancellationToken);
}
