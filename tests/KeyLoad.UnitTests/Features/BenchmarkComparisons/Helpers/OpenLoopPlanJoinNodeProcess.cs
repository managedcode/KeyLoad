using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanJoinNodeProcess
{
    private const string NodeCommand = "node";
    private const string CliEntry = "scripts/Features/BenchmarkComparisons/isolated-plan.mjs";
    private const string OpenLoopModule = "scripts/Features/BenchmarkComparisons/open-loop-isolated-plan.mjs";
    private const string PathEnvironment = "PATH";
    private const string InputTypeArgument = "--input-type=module";
    private const string EvalArgument = "-e";

    internal static Task<OpenLoopPlanNodeResult> RunCliAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var start = CreateStartInfo();
        start.ArgumentList.Add(Path.Combine(OpenLoopPlanNodeProcess.RepositoryRoot, CliEntry));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        return OpenLoopPlanNodeProcess.RunOwnedAsync(executionOptions, start, input: null,
            keepStandardInputOpen: false, ready: null, cancellationToken: cancellationToken);
    }

    internal static Task<OpenLoopPlanNodeResult> RunMatrixProbeAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, string candidateJson,
        CancellationToken cancellationToken)
    {
        var start = CreateStartInfo();
        start.ArgumentList.Add(InputTypeArgument);
        start.ArgumentList.Add(EvalArgument);
        start.ArgumentList.Add(OpenLoopPlanMatrixNodeProgram.Source);
        start.Environment[OpenLoopPlanNodeProgram.ModuleEnvironment] = Path.Combine(
            OpenLoopPlanNodeProcess.RepositoryRoot, OpenLoopModule);
        return OpenLoopPlanNodeProcess.RunOwnedAsync(executionOptions, start, candidateJson,
            keepStandardInputOpen: false, ready: null, cancellationToken: cancellationToken);
    }

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
        {
            start.Environment[PathEnvironment] = path;
        }
        return start;
    }
}
