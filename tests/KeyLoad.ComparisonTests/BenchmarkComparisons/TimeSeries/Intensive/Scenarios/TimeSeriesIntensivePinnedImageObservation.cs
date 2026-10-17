namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageObservation(TimeSeriesIntensivePinnedImageConfig Image,
    IReadOnlyDictionary<string, string> Facts, string ContainerId);
