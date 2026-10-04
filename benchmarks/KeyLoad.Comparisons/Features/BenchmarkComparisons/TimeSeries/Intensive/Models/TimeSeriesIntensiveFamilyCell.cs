using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensiveFamilyCell(string Id, TimeSeriesIntensiveSelection Selection);

internal sealed record TimeSeriesIntensiveFamilyCells(
    ImmutableArray<TimeSeriesIntensiveFamilyCell> Preflight,
    ImmutableArray<TimeSeriesIntensiveFamilyCell> Intensive);
