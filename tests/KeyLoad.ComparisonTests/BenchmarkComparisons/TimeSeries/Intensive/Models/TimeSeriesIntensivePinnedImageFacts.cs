namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageFacts(int SchemaVersion,
    TimeSeriesIntensivePinnedImageContext Context, TimeSeriesIntensivePinnedImageConfig Image,
    IReadOnlyDictionary<string, string> Observations, string ContainerName, string ContainerId,
    bool CleanupCompleted, IReadOnlyList<TimeSeriesIntensivePinnedImageCommandReceipt> Commands);
