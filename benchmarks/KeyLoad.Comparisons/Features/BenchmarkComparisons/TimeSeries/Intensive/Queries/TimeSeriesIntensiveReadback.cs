namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensiveReadback(string SeriesId, DateTimeOffset From, DateTimeOffset Until, int ExpectedCount);
