namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageCommandReceipt(string Executable,
    IReadOnlyList<string> Arguments, int DeadlineSeconds, int? ExitCode,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    TimeSeriesIntensivePinnedImageOutputReceipt Output, TimeSeriesIntensivePinnedImageOutputReceipt Error,
    IReadOnlyList<string> Failures);
