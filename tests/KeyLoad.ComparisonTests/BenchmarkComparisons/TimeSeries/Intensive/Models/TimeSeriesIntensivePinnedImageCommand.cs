namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageCommand(string Executable, IReadOnlyList<string> Arguments,
    int DeadlineSeconds, int? ExitCode, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    byte[] Output, byte[] Error, IReadOnlyList<string> Failures);
