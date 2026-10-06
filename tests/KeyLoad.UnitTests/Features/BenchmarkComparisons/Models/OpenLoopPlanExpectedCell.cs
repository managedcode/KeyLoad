namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed record OpenLoopPlanExpectedCell(string Id, string Target, int NodeCount, string Scenario,
    string Profile, string Family, int OfferedRatePerSecond, bool CancellationProof);
