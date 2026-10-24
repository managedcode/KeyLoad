namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal enum TimeSeriesIntensiveFailureOrigin
{
    None,
    KeyLoad,
    PostgreSQL,
    HttpTransport,
    Client,
    Oracle,
    Unexpected
}
