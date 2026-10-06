using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class CurrentGitHubJobSelectionNodeProcess
{
    private const string NodeCommand = "node";
    private const string InputTypeArgument = "--input-type=module";
    private const string EvalArgument = "-e";
    private const string PathEnvironment = "PATH";

    internal static Task<OpenLoopPlanNodeResult> RunAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(NodeCommand)
        {
            WorkingDirectory = OpenLoopPlanNodeProcess.RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(InputTypeArgument);
        start.ArgumentList.Add(EvalArgument);
        start.ArgumentList.Add(CurrentGitHubJobSelectionNodeProgram.Source);

        var path = Environment.GetEnvironmentVariable(PathEnvironment);
        start.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(path))
        {
            start.Environment[PathEnvironment] = path;
        }
        start.Environment[CurrentGitHubJobSelectionTestProtocol.RootEnvironment] = OpenLoopPlanNodeProcess.RepositoryRoot;

        return OpenLoopPlanNodeProcess.RunOwnedAsync(OpenLoopPlanProcessOptionsBinding.Capture(), start,
            input: null, keepStandardInputOpen: false, ready: null, cancellationToken);
    }
}
