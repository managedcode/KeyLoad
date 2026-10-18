using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubNativeProcess
{
    internal static async Task<NativeToolProcessResult> RunAsync(CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo() };
        if (!process.Start())
        {
            throw new InvalidOperationException(IsolatedGitHubNativeProtocol.Failure);
        }

        using var deadlineTimeout = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.GitHubNativeProcessTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var output = NativeToolProcessOutput.ReadAsync(process.StandardOutput, deadline.Token);
        var error = NativeToolProcessOutput.ReadAsync(process.StandardError, deadline.Token);
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
                await NativeToolProcessOutput.ObserveAsync(output);
                await NativeToolProcessOutput.ObserveAsync(error);
            }
        }
    }

    private static ProcessStartInfo StartInfo()
    {
        var root = SessionNodeProcess.RepositoryRoot();
        var start = new ProcessStartInfo(IsolatedGitHubNativeProtocol.Node)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(SessionNodeProcess.Module(IsolatedGitHubNativeProtocol.Entry));
        return start;
    }

    private static async Task ReapAsync(Process process)
    {
        TryKill(process);
        using var cleanup = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.GitHubNativeProcessCleanupTimeout, TimeProvider.System);
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(IsolatedGitHubNativeProtocol.Failure);
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
