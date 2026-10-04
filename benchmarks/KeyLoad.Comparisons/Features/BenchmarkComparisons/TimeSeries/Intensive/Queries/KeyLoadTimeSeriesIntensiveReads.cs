using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed partial class KeyLoadTimeSeriesIntensiveTarget
{
    public async Task<ImmutableArray<SampleRecord>> ReadAsync(string seriesId, DateTimeOffset from,
        DateTimeOffset until, int limit, CancellationToken cancellationToken)
    {
        EnsureOpen();
        var result = await Context.Peers[KeyLoadTimeSeriesIntensiveProtocol.MeasurementIngressIndex]
            .Client.ReadSamplesAsync(
            new(Context.Partition, Context.SeriesSet, seriesId, from, until, limit), cancellationToken)
            .ConfigureAwait(false);
        return KeyLoadTimeSeriesIntensiveResult.Raw(result);
    }

    public async Task<SampleRecord?> LatestAsync(string seriesId, DateTimeOffset? atOrBefore,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        var result = await Context.Peers[KeyLoadTimeSeriesIntensiveProtocol.MeasurementIngressIndex]
            .Client.ReadLatestSampleAsync(
            new(Context.Partition, Context.SeriesSet, seriesId, atOrBefore), cancellationToken)
            .ConfigureAwait(false);
        return KeyLoadTimeSeriesIntensiveResult.Latest(result);
    }

    public async Task<SampleAggregate> AggregateAsync(string seriesId, DateTimeOffset from,
        DateTimeOffset? untilExclusive, int maxSamples, CancellationToken cancellationToken)
    {
        EnsureOpen();
        var result = await Context.Peers[KeyLoadTimeSeriesIntensiveProtocol.MeasurementIngressIndex]
            .Client.AggregateSamplesAsync(
            new(Context.Partition, Context.SeriesSet, seriesId, from, untilExclusive, maxSamples), cancellationToken)
            .ConfigureAwait(false);
        return KeyLoadTimeSeriesIntensiveResult.Value(result);
    }

    public async Task<ImmutableArray<SampleAggregateWindow>> WindowsAsync(string seriesId, DateTimeOffset from,
        DateTimeOffset? untilExclusive, TimeSpan width, int maxSamples, int maxWindows,
        CancellationToken cancellationToken)
    {
        EnsureOpen();
        var result = await Context.Peers[KeyLoadTimeSeriesIntensiveProtocol.MeasurementIngressIndex]
            .Client.AggregateSampleWindowsAsync(
            new(Context.Partition, Context.SeriesSet, seriesId, from, untilExclusive, width, maxSamples, maxWindows),
            cancellationToken).ConfigureAwait(false);
        return KeyLoadTimeSeriesIntensiveResult.Windows(result);
    }
}
