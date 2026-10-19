using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubProcess
{
    internal static async Task<ToolProcessResult> RunAsync(CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo() };
        if (!process.Start())
        {
            throw new InvalidOperationException(IsolatedGitHubProtocol.Failure);
        }

        using var deadlineTimeout = new CancellationTokenSource(ExecutionPolicyFixture.Harness().Value.GitHubProcessTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var output = ToolProcessOutput.ReadAsync(process.StandardOutput, deadline.Token);
        var error = ToolProcessOutput.ReadAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            try
            {
                await ReapAsync(process);
            }
            finally
            {
                await deadline.CancelAsync();
                await ToolProcessOutput.ObserveAsync(output);
                await ToolProcessOutput.ObserveAsync(error);
            }
        }
    }

    private static ProcessStartInfo StartInfo()
    {
        var root = SessionNodeProcess.RepositoryRoot();
        var start = new ProcessStartInfo(IsolatedGitHubProtocol.Node)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(SessionNodeProcess.Module(IsolatedGitHubProtocol.Entry));
        return start;
    }

    private static async Task ReapAsync(Process process)
    {
        TryKill(process);
        using var cleanup = new CancellationTokenSource(ExecutionPolicyFixture.Harness().Value.GitHubProcessCleanupTimeout, TimeProvider.System);
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(IsolatedGitHubProtocol.Failure);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The actual owned CLI exited between observation and kill.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // The operating system already reaped this owned CLI.
        }
    }
}
