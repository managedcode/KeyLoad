using System.Collections.Immutable;
using System.Globalization;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.TimeSeries;

/// <summary>
/// KeyLoad time series through the SDK: a <see cref="ResourceKind.TimeSeries"/> resource, one <see cref="AppendSamples"/>
/// commit per batch, bounded <c>ReadSamplesAsync</c> pages, and the server-side <c>AggregateSamplesAsync</c> and
/// <c>AggregateSampleWindowsAsync</c> statistics.
/// </summary>
internal sealed class KeyLoadTimeSeriesDriver(BenchmarkConnection connection) : ITimeSeriesDriver
{
    private const string Set = "points";
    private const int Batch = 1_000;
    private const int PageLimit = 1_000;
    private const int NextTick = 1;
    private readonly KeyLoadDatabase database = new(connection);

    public int BatchSize => Batch;

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(Set, ResourceKind.TimeSeries), token);

    public async Task AppendAsync(string series, IReadOnlyList<TimePoint> points, CancellationToken token)
    {
        ImmutableArray<SampleData> values = [.. points.Select(point => new SampleData(
            point.At.UtcTicks.ToString(CultureInfo.InvariantCulture), point.At.ToUniversalTime(), point.Value))];
        _ = await database.CommitAsync([new AppendSamples(Set, series, values)], token);
    }

    /// <remarks>The SDK range end is inclusive, so the read ends one tick before <paramref name="until"/>.</remarks>
    public async Task<IReadOnlyList<TimePoint>> ReadRangeAsync(string series, DateTimeOffset from, DateTimeOffset until,
        CancellationToken token)
    {
        var last = until.AddTicks(-NextTick);
        var found = new List<TimePoint>();
        var next = from;
        while (next <= last)
        {
            var page = KeyLoadDatabase.Require(await database.Client.ReadSamplesAsync(
                new(database.Partition, Set, series, next, last, PageLimit), token), nameof(ReadRangeAsync));
            found.AddRange(page.Select(record => new TimePoint(record.Sample.Timestamp, record.Sample.Value)));
            if (page.Length < PageLimit)
            {
                break;
            }
            next = page[^1].Sample.Timestamp.AddTicks(NextTick);
        }
        return found;
    }

    public async Task<SeriesAggregate> AggregateAsync(string series, DateTimeOffset from, DateTimeOffset until,
        CancellationToken token)
    {
        var found = KeyLoadDatabase.Require(await database.Client.AggregateSamplesAsync(
            new(database.Partition, Set, series, from, until), token), nameof(AggregateAsync));
        return new(found.Count, found.Sum, found.Minimum ?? double.NaN, found.Maximum ?? double.NaN);
    }

    public async Task<IReadOnlyList<SeriesWindow>> WindowsAsync(string series, DateTimeOffset from, DateTimeOffset until,
        TimeSpan width, CancellationToken token)
    {
        var found = KeyLoadDatabase.Require(await database.Client.AggregateSampleWindowsAsync(
            new(database.Partition, Set, series, from, until, width), token), nameof(WindowsAsync));
        return [.. found.Windows.Select(window => new SeriesWindow(window.From, window.Aggregate.Count, window.Aggregate.Sum))];
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
