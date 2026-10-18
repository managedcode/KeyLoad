namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record RawStorageReportProbeResponse(int ExitCode, string StandardError,
    RawStorageReportCaseResult[] Cases);
