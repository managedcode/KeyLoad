using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class FailedBenchmarkNodeProcess
{
    internal static async Task<ImageBundleRealResult> RunAsync(string source, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(source) };
        if (!process.Start())
        {
            throw new InvalidOperationException(ImageBundleRealProtocol.Failure);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(NativeExecutionPolicyFixture.Harness().Value.ImageBundleProcessTimeout);
        var output = ImageBundleRealOutput.ReadAsync(process.StandardOutput, deadline.Token);
        var error = ImageBundleRealOutput.ReadAsync(process.StandardError, deadline.Token);
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
                await ImageBundleRealOutput.ObserveAsync(output);
                await ImageBundleRealOutput.ObserveAsync(error);
            }
        }
    }

    private static ProcessStartInfo StartInfo(string source)
    {
        var root = ImageBundleRealProtocol.RepositoryRoot();
        var start = new ProcessStartInfo(ImageBundleRealProtocol.Node)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--input-type=module");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(source);
        return start;
    }

    private static async Task ReapAsync(Process process)
    {
        TryKill(process);
        using var cleanup = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.ImageBundleCleanupTimeout);
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(ImageBundleRealProtocol.Failure);
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
            // The owned child exited between observation and kill.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // The operating system already reaped the owned child.
        }
    }
}
