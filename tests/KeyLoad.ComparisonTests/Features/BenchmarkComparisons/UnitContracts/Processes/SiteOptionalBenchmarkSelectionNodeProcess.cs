namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SiteOptionalBenchmarkSelectionNodeProcess
{
    internal static Task<IsolatedAggregateNodeResult> RunAsync(string scenario, CancellationToken cancellationToken)
        => IsolatedAggregateNodeProcess.RunAsync(
            ["--input-type=module", "-e", SiteOptionalBenchmarkSelectionNodeProgram.Source,
                IsolatedAggregateNodeProcess.Module("site-isolated-github-context.mjs"), scenario], cancellationToken);
}
