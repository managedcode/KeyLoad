namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal interface ITimeSeriesPersistentTarget : IAsyncDisposable
{
    TimeSeriesTargetMetadata Metadata
    {
        get;
    }

    Task InitializeAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken);

    Task SeedAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken);

    Task<TimeSeriesReadResult> ReadAsync(TimeSeriesComparisonWorkload workload, TimeSeriesReadRange range,
        CancellationToken cancellationToken);
}
