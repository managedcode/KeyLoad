namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensiveReadback(string SeriesId, DateTimeOffset From, DateTimeOffset Until, int ExpectedCount);
