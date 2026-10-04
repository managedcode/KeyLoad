namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageConfig(string Reference, string ConfigId,
    IReadOnlyList<string> RepoDigests, string Os, string Architecture, IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> Command, string DefaultUser, IReadOnlyList<string> DefaultEnvironment);

internal sealed record TimeSeriesIntensivePinnedImageFacts(int SchemaVersion,
    TimeSeriesIntensivePinnedImageContext Context, TimeSeriesIntensivePinnedImageConfig Image,
    IReadOnlyDictionary<string, string> Observations, string ContainerName, string ContainerId,
    bool CleanupCompleted, IReadOnlyList<TimeSeriesIntensivePinnedImageCommandReceipt> Commands);

internal sealed record TimeSeriesIntensivePinnedImageCommand(string Executable, IReadOnlyList<string> Arguments,
    int DeadlineSeconds, int? ExitCode, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    byte[] Output, byte[] Error, IReadOnlyList<string> Failures);

internal sealed record TimeSeriesIntensivePinnedImageOutputReceipt(string Path, int Bytes, string Sha256);

internal sealed record TimeSeriesIntensivePinnedImageCommandReceipt(string Executable,
    IReadOnlyList<string> Arguments, int DeadlineSeconds, int? ExitCode,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    TimeSeriesIntensivePinnedImageOutputReceipt Output, TimeSeriesIntensivePinnedImageOutputReceipt Error,
    IReadOnlyList<string> Failures);
