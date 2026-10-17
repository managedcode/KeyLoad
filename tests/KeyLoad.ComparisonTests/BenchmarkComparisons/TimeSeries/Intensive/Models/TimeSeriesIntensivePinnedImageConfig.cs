namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageConfig(string Reference, string ConfigId,
    IReadOnlyList<string> RepoDigests, string Os, string Architecture, IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> Command, string DefaultUser, IReadOnlyList<string> DefaultEnvironment);
