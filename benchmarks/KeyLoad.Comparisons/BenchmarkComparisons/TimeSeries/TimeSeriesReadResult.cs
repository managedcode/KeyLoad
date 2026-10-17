using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed record TimeSeriesReadResult(bool Succeeded, ImmutableArray<TimeSeriesSamplePoint> Samples,
    string? ErrorCode);
