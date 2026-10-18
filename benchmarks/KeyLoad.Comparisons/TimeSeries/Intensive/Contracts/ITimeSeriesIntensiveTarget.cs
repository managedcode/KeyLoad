using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal interface ITimeSeriesIntensiveTarget : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task SeedAsync(string seriesId, ImmutableArray<SampleData> samples, string tagsJson, CancellationToken cancellationToken);
    Task<TimeSeriesIntensiveAppendReceipt> AppendAsync(string seriesId, Guid commandId, SampleData sample, string tagsJson, CancellationToken cancellationToken);
    Task<ImmutableArray<SampleRecord>> ReadAsync(string seriesId, DateTimeOffset from, DateTimeOffset until, int limit, CancellationToken cancellationToken);
    Task<SampleRecord?> LatestAsync(string seriesId, DateTimeOffset? atOrBefore, CancellationToken cancellationToken);
    Task<SampleAggregate> AggregateAsync(string seriesId, DateTimeOffset from, DateTimeOffset? untilExclusive, int maxSamples, CancellationToken cancellationToken);
    Task<ImmutableArray<SampleAggregateWindow>> WindowsAsync(string seriesId, DateTimeOffset from, DateTimeOffset? untilExclusive, TimeSpan width, int maxSamples, int maxWindows, CancellationToken cancellationToken);
}
