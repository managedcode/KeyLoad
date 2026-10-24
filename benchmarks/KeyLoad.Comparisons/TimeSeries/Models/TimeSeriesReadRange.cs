namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries;

internal sealed record TimeSeriesReadRange(string Name, DateTimeOffset From, DateTimeOffset Until,
    int ExpectedCount, string? ExpectedErrorCode = null);
