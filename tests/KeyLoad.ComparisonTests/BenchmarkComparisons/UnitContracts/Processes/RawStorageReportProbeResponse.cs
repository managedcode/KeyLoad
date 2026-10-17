namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record RawStorageReportProbeResponse(int ExitCode, string StandardError,
    RawStorageReportCaseResult[] Cases);
