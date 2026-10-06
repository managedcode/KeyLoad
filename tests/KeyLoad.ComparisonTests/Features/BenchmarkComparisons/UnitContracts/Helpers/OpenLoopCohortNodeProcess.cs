using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopCohortNodeProcess
{
    private const string NodeCommand = "node";
    private const string PathEnvironment = "PATH";
    private const string InputTypeArgument = "--input-type=module";
    private const string EvalArgument = "-e";
    private const string CliEntry = "scripts/Features/BenchmarkComparisons/open-loop-cohort-aggregate-cli.mjs";
    private const string PlanEntry = "scripts/Features/BenchmarkComparisons/open-loop-isolated-plan.mjs";

    internal static Task<OpenLoopPlanNodeResult> SeedFailedCohortAsync(
        OpenLoopCohortStageEvidence evidence, string root, CancellationToken cancellationToken)
    {
        var start = CreateStartInfo();
        start.ArgumentList.Add(InputTypeArgument);
        start.ArgumentList.Add(EvalArgument);
        start.ArgumentList.Add(OpenLoopCohortNodeProgram.Source);
        start.Environment[OpenLoopCohortNodeProgram.RootEnvironment] = root;
        start.Environment[OpenLoopCohortNodeProgram.ModuleEnvironment] = Path.Combine(
            OpenLoopPlanNodeProcess.RepositoryRoot, PlanEntry);
        return RunAsync(evidence, start, OpenLoopCohortStage.SeedFailedCohort, cancellationToken);
    }

    internal static Task<OpenLoopPlanNodeResult> AggregateAsync(OpenLoopCohortStageEvidence evidence,
        string input, string output, OpenLoopCohortStage stage, CancellationToken cancellationToken)
    {
        var start = CreateStartInfo();
        start.ArgumentList.Add(Path.Combine(OpenLoopPlanNodeProcess.RepositoryRoot, CliEntry));
        start.ArgumentList.Add("--input=" + input);
        start.ArgumentList.Add("--output=" + output);
        return RunAsync(evidence, start, stage, cancellationToken);
    }

    private static Task<OpenLoopPlanNodeResult> RunAsync(OpenLoopCohortStageEvidence evidence,
        ProcessStartInfo start, OpenLoopCohortStage stage, CancellationToken cancellationToken)
        => OpenLoopPlanNodeProcess.RunObservedAsync(evidence.ExecutionOptions, start, evidence, stage, cancellationToken);

    private static ProcessStartInfo CreateStartInfo()
    {
        var root = OpenLoopPlanNodeProcess.RepositoryRoot;
        var start = new ProcessStartInfo(NodeCommand)
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var path = Environment.GetEnvironmentVariable(PathEnvironment);
        start.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(path))
        { start.Environment[PathEnvironment] = path; }
        return start;
    }
}
