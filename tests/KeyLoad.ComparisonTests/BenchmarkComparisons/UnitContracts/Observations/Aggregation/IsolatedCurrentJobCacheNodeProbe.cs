namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedCurrentJobCacheNodeProbe
{
    internal static Task<IsolatedAggregateNodeResult> RunPolicyAsync(CancellationToken cancellationToken)
    {
        var root = SessionNodeProcess.RepositoryRoot();
        var fixtureDirectory = Path.Combine(root, "tests", "KeyLoad.ComparisonTests", "BenchmarkComparisons", "UnitContracts", "Fixtures");
        return RunAsync(["policy", SessionNodeProcess.Module("isolated-current-job.mjs"),
            SessionNodeProcess.Module("isolated-github-transport.mjs"), fixtureDirectory], cancellationToken);
    }

    internal static Task<IsolatedAggregateNodeResult> RunRefreshWaitCancellationAsync(CancellationToken cancellationToken)
        => RunAsync(["wait-cancel", SessionNodeProcess.Module("isolated-current-job.mjs")], cancellationToken);

    internal static Task<IsolatedAggregateNodeResult> RunOwnedChildAsync(string outputPath, string markerPath,
        string readyPath, string mode, int maximumBytes, CancellationToken cancellationToken)
        => RunAsync(["owned-child", SessionNodeProcess.Module("isolated-github-stream.mjs"),
            outputPath, markerPath, readyPath, mode, maximumBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            cancellationToken);

    private static Task<IsolatedAggregateNodeResult> RunAsync(string[] operationArguments,
        CancellationToken cancellationToken)
        => SessionNodeProcess.RunAsync(["--input-type=module", "-e", IsolatedCurrentJobCacheNodeProgram.Source,
            .. operationArguments], cancellationToken);
}
