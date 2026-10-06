using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ImageBundleRealProcess
{
    internal static async Task<ImageBundleRealResult> RunAsync(CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo() };
        if (!process.Start())
        {
            throw new InvalidOperationException(ImageBundleRealProtocol.Failure);
        }

        using var deadlineTimeout = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.ImageBundleProcessTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
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

    private static ProcessStartInfo StartInfo()
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
        start.ArgumentList.Add(Path.Combine(root, ImageBundleRealProtocol.Scripts, ImageBundleRealProtocol.Features,
            ImageBundleRealProtocol.Slice, ImageBundleRealProtocol.Entry));
        return start;
    }

    private static async Task ReapAsync(Process process)
    {
        TryKill(process);
        using var cleanup = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.ImageBundleCleanupTimeout, TimeProvider.System);
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
