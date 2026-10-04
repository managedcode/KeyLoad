namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

// Until is inclusive for raw reads and exclusive for aggregate/window reads.
internal sealed record TimeSeriesIntensiveReadPlan(DateTimeOffset From, DateTimeOffset Until, DateTimeOffset LatestAtOrBefore);
