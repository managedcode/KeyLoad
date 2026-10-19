namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record SerializationReportResponse(int ExitCode, string StandardError,
    bool Accepted, bool Unchanged, string Error);
