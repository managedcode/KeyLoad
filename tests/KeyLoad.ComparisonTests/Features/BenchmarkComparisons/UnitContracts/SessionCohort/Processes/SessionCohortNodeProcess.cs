namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Runs actual session contracts through the shared bounded owned Node process.</summary>
internal static class SessionCohortNodeProcess
{
    internal static Task<IsolatedAggregateNodeResult> RunAsync(string scenario, CancellationToken token)
        => SessionNodeProcess.RunAsync([Path.Combine(SessionNodeProcess.RepositoryRoot(),
            "tests/KeyLoad.ComparisonTests/Features/BenchmarkComparisons/UnitContracts/SessionCohort/Fixtures/session-contract-probe.mjs"), scenario], token);
}
