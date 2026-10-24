namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries;

internal sealed record TimeSeriesSamplePoint(string EventId, DateTimeOffset Timestamp, double Value, long Sequence,
    string TagsJson);
