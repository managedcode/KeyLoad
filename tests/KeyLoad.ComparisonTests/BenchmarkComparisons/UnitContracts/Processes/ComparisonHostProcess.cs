using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Runs the Release comparison host with isolated benchmark configuration.</summary>
internal static class ComparisonHostProcess
{
    private const string StartupFailureMessage = "The comparison host did not start.";
    private const int StartupTimeoutSeconds = 20;
    private const int CleanupTimeoutSeconds = 5;

    internal static async Task<ComparisonHostExit> RunAsync(string[] arguments,
        IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = ComparisonHostLaunchSettings.Create(arguments, environment);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(StartupFailureMessage);
        }

        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(StartupTimeoutSeconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        using var captureLifetime = new CancellationTokenSource();
        var stdout = new ComparisonHostOutputCapture();
        var stderr = new ComparisonHostOutputCapture();
        var stdoutTask = stdout.CaptureAsync(process.StandardOutput, captureLifetime.Token);
        var stderrTask = stderr.CaptureAsync(process.StandardError, captureLifetime.Token);
        var captures = Task.WhenAll(stdoutTask, stderrTask);
        var stage = ComparisonHostStartupStage.Unavailable;
        try
        {
            stage = ComparisonHostStartupStage.ProcessExit;
            await process.WaitForExitAsync(deadline.Token);
            stage = ComparisonHostStartupStage.OutputDrain;
            var text = await captures.WaitAsync(deadline.Token);
            return new(process.ExitCode, text[0], text[1]);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var diagnostics = ComparisonHostStartupDiagnostics.Create(stage, process,
                stdout.Snapshot(), stderr.Snapshot());
            throw ComparisonHostStartupDiagnostics.CreateTimeoutException(diagnostics);
        }
        finally
        {
            await CleanupAsync(process, captureLifetime, captures);
        }
    }

    private static async Task ObserveCaptureAsync(Task capture)
    {
        try
        {
            await capture.WaitAsync(TimeSpan.FromSeconds(CleanupTimeoutSeconds), TimeProvider.System);
        }
        catch (OperationCanceledException)
        {
            // Cancellation released an active pipe read.
        }
        catch (ObjectDisposedException)
        {
            // Closing the stream released an active pipe read.
        }
        catch (IOException)
        {
            // Process termination closed an active pipe read.
        }
        catch (TimeoutException)
        {
            _ = capture.ContinueWith(static completed => _ = completed.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static async Task ObserveExitAsync(Process process)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds), TimeProvider.System);
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
        {
            // Exit observation reached its cleanup bound.
        }
    }

    private static async Task CleanupAsync(Process process, CancellationTokenSource captureLifetime,
        Task captures)
    {
        try
        {
            KillIfRunning(process);
            await ObserveExitAsync(process);
        }
        finally
        {
            await StopCapturesAsync(process, captureLifetime, captures);
        }
    }

    private static async Task StopCapturesAsync(Process process, CancellationTokenSource captureLifetime, Task captures)
    {
        var cancellation = captureLifetime.CancelAsync();
        try
        {
            CloseReader(process.StandardOutput);
            CloseReader(process.StandardError);
        }
        finally
        {
            try
            {
                await ObserveCaptureAsync(cancellation);
            }
            finally
            {
                await ObserveCaptureAsync(captures);
            }
        }
    }

    private static void CloseReader(StreamReader reader)
    {
        try
        {
            reader.Dispose();
        }
        catch (IOException)
        {
            // A terminated pipe can fail while closing.
        }
        catch (ObjectDisposedException)
        {
            // Cancellation can close a reader concurrently.
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The child exited concurrently with cleanup.
        }
    }
}
