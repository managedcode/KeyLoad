namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopWorkerFinalizeNodeProcess
{
    private const string PlanModule = "open-loop-isolated-plan.mjs";

    internal static Task<IsolatedAggregateNodeResult> RunAsync(string workspace, CancellationToken cancellationToken)
        => IsolatedAggregateNodeProcess.RunAsync(
        ["--input-type=module", "-e", OpenLoopWorkerFinalizeNodeProgram.Source,
            IsolatedAggregateNodeProcess.Module(PlanModule), workspace], cancellationToken);
}
