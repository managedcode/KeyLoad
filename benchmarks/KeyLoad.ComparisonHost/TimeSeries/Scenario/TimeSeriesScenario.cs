using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.TimeSeries;

/// <summary>The time-series scenario, shared by every time-series database and KeyLoad.</summary>
internal static class TimeSeriesScenario
{
    internal const string Load = "load";
    internal const string RangeRead = "range-read";
    internal const string Aggregate = "aggregate";
    internal const string Windows = "windows";
    internal const int RangePoints = 100;
    internal const int AggregatePoints = 600;
    internal static readonly TimeSpan WindowWidth = TimeSpan.FromMinutes(1);
    private const string WrongResult = "Wrong time-series result for series ";

    /// <summary>Each call appends one batch of consecutive points of one series; batches divide the series evenly.</summary>
    internal static async Task<CaseResult> LoadAsync(ITimeSeriesDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var points = new TimeSeriesDataset(settings.Records).PointsPerSeries;
        var batch = Math.Min(driver.BatchSize, points);
        if (points < AggregatePoints || points % batch != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Records, null);
        }
        await driver.PrepareAsync(token);
        return await OperationLanes.RunBatchesAsync(Load, TimeSeriesDataset.SeriesCount * points, batch, settings.Clients,
            (first, count, cancellation) =>
            {
                var series = first / points;
                return driver.AppendAsync(TimeSeriesDataset.SeriesId(series), [.. Enumerable.Range(first % points, count)
                    .Select(point => TimeSeriesDataset.Point(series, point))], cancellation);
            }, token);
    }

    internal static Task<CaseResult> RangeReadAsync(ITimeSeriesDriver driver, BenchmarkSettings settings, CancellationToken token)
        => Run(RangeRead, settings, RangePoints, async (series, from, cancellation) =>
        {
            var points = await driver.ReadRangeAsync(TimeSeriesDataset.SeriesId(series), TimeSeriesDataset.At(from),
                TimeSeriesDataset.At(from + RangePoints), cancellation);
            return points.Count == RangePoints && points.All(point => TimeSeriesDataset.Close(point.Value,
                TimeSeriesDataset.Value(series, (int)(point.At - TimeSeriesDataset.Epoch).TotalSeconds)));
        }, token);

    internal static Task<CaseResult> AggregateAsync(ITimeSeriesDriver driver, BenchmarkSettings settings, CancellationToken token)
        => Run(Aggregate, settings, AggregatePoints, async (series, from, cancellation) =>
        {
            var found = await driver.AggregateAsync(TimeSeriesDataset.SeriesId(series), TimeSeriesDataset.At(from),
                TimeSeriesDataset.At(from + AggregatePoints), cancellation);
            var expected = TimeSeriesDataset.Aggregate(series, from, AggregatePoints);
            return found.Count == expected.Count && TimeSeriesDataset.Close(found.Sum, expected.Sum)
                && TimeSeriesDataset.Close(found.Min, expected.Min) && TimeSeriesDataset.Close(found.Max, expected.Max);
        }, token);

    /// <summary>One-minute windows over a whole series.</summary>
    internal static Task<CaseResult> WindowsAsync(ITimeSeriesDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var points = new TimeSeriesDataset(settings.Records).PointsPerSeries;
        var width = (int)WindowWidth.TotalSeconds;
        return Run(Windows, settings, points, async (series, _, cancellation) =>
        {
            var windows = await driver.WindowsAsync(TimeSeriesDataset.SeriesId(series), TimeSeriesDataset.Epoch,
                TimeSeriesDataset.At(points), WindowWidth, cancellation);
            return windows.Count == (points + width - 1) / width && windows.All(window =>
            {
                var first = (int)(window.Start - TimeSeriesDataset.Epoch).TotalSeconds;
                var expected = TimeSeriesDataset.Aggregate(series, first, Math.Min(width, points - first));
                return window.Count == expected.Count && TimeSeriesDataset.Close(window.Sum, expected.Sum);
            });
        }, token);
    }

    private static Task<CaseResult> Run(string id, BenchmarkSettings settings, int span,
        Func<int, int, CancellationToken, Task<bool>> query, CancellationToken token)
    {
        var points = new TimeSeriesDataset(settings.Records).PointsPerSeries;
        return OperationLanes.RunAsync(id, settings.Operations, settings.Clients, async (index, cancellation) =>
        {
            var series = settings.Spread(index, 0, TimeSeriesDataset.SeriesCount);
            var from = span >= points ? 0 : settings.Spread(index, 0, points - span);
            if (!await query(series, from, cancellation))
            {
                throw new InvalidDataException(WrongResult + TimeSeriesDataset.SeriesId(series));
            }
        }, token);
    }
}
