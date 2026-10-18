using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteProcessCleanup
{
    private const string ExitTimeout = "The owned site process did not exit within its cleanup deadline.";

    internal static async Task StopAsync(Process process)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(SiteTokens.CleanupTimeoutMilliseconds), TimeProvider.System);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(deadline.Token);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // The child exited between the state check and termination.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // The native process exited concurrently with termination.
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(ExitTimeout);
        }
    }

    internal static async Task ObserveCapturesAsync(Process process, Task<string> stdout, Task<string> stderr)
    {
        CloseReader(process.StandardOutput);
        CloseReader(process.StandardError);
        var captures = Task.WhenAll(stdout, stderr);
        try
        {
            await captures.WaitAsync(TimeSpan.FromMilliseconds(SiteTokens.CleanupTimeoutMilliseconds), TimeProvider.System);
        }
        catch (OperationCanceledException) when (captures.IsCanceled)
        {
            // The original caller deadline cancelled pipe reads.
        }
        catch (IOException) when (HasOnlyExpectedCaptureFailures(captures))
        {
            // Terminating the child closed a pipe.
        }
        catch (ObjectDisposedException) when (HasOnlyExpectedCaptureFailures(captures))
        {
            // Closing owned readers released a pending read.
        }
        catch (InvalidOperationException error) when ((error.Message == SiteTokens.NodeOutputExceeded
            || error.Message == SiteTokens.BuilderOutputExceeded) && HasOnlyExpectedCaptureFailures(captures))
        {
            // The original bounded-output failure is rethrown by its caller.
        }
        catch (TimeoutException)
        {
            _ = captures.ContinueWith(static completed => _ = completed.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private static bool HasOnlyExpectedCaptureFailures(Task captures)
        => captures.Exception is { } errors && errors.InnerExceptions.All(IsExpectedCaptureFailure);

    private static bool IsExpectedCaptureFailure(Exception error)
        => error is IOException or ObjectDisposedException or OperationCanceledException
            || error is InvalidOperationException invalid && (invalid.Message == SiteTokens.NodeOutputExceeded
                || invalid.Message == SiteTokens.BuilderOutputExceeded);

    private static void CloseReader(StreamReader reader)
    {
        try
        {
            reader.Dispose();
        }
        catch (IOException)
        {
            // The terminated child has already closed the pipe.
        }
        catch (ObjectDisposedException)
        {
            // The owned reader was closed concurrently.
        }
    }
}
