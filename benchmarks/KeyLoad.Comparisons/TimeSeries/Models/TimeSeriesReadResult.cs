using System.Collections.Immutable;

namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries;

internal sealed record TimeSeriesReadResult(bool Succeeded, ImmutableArray<TimeSeriesSamplePoint> Samples,
    string? ErrorCode);
