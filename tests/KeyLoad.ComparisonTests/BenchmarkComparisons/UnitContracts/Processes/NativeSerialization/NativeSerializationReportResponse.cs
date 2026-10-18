namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record NativeSerializationReportResponse(int ExitCode, string StandardError,
    bool Accepted, bool Unchanged, string Error);
