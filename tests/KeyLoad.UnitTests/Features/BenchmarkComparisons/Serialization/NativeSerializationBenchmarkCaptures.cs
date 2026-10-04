using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Settles both borrowed-stream pumps before returning ownership to the caller.</summary>
internal static class NativeSerializationBenchmarkCaptures
{
    internal static async Task<int> RunAsync(Process process, Stream stdout, Stream stderr, CancellationToken cancellationToken)
    {
        EnsureStarted(process);
        using var capturesCancellation = new CancellationTokenSource();
        var (stdoutSource, stdoutDestination) = (process.StandardOutput.BaseStream, stdout);
        var (stderrSource, stderrDestination) = (process.StandardError.BaseStream, stderr);
        var stdoutCapture = CaptureAsync(stdoutSource, stdoutDestination, capturesCancellation.Token);
        var stderrCapture = CaptureAsync(stderrSource, stderrDestination, capturesCancellation.Token);
        var captures = Task.WhenAll(stdoutCapture, stderrCapture);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var failures = new NativeSerializationBenchmarkFailures();
        await ObserveExecutionAsync(process, captures, deadline, failures, cancellationToken);
        try
        {
            await StopAndObserveAsync(process, capturesCancellation, captures, failures);
        }
        finally
        {
            try
            {
                await stdoutCapture;
            }
            catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
            {
                failures.AddTaskFailures(stdoutCapture, failure);
            }
            try
            {
                await stderrCapture;
            }
            catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
            {
                failures.AddTaskFailures(stderrCapture, failure);
            }
            finally
            {
                try
                {
                    capturesCancellation.Dispose();
                }
                catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
                {
                    failures.Add(failure);
                }
            }
        }
        failures.ThrowIfAny();
        return process.ExitCode;
    }

    private static async Task ObserveExecutionAsync(Process process, Task captures, CancellationTokenSource deadline,
        NativeSerializationBenchmarkFailures failures, CancellationToken cancellationToken)
    {
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await process.WaitForExitAsync(execution.Token);
            await captures.WaitAsync(execution.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            failures.Add(new TimeoutException("The generated native benchmark consumer exceeded its deadline."));
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    private static async Task StopAsync(Process process, CancellationTokenSource cancellation, Task captures,
        NativeSerializationBenchmarkFailures failures)
    {
        Task? exit = null;
        try
        {
            exit = process.WaitForExitAsync();
            await NativeSerializationBenchmarkProcessExit.ObserveDiagnosticAsync(process, exit, failures);
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
            NativeSerializationBenchmarkProcessExit.RetryKill(process, failures);
        }
        finally
        {
            await CancelCapturesAsync(cancellation, failures);
            await ObserveCapturesAsync(process, cancellation, captures, failures);
            try
            {
                if (exit is not null)
                {
                    await exit;
                }
            }
            catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
            {
                failures.AddTaskFailures(exit!, failure);
            }
        }
    }

    private static async Task ObserveCapturesAsync(Process process, CancellationTokenSource cancellation, Task captures,
        NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            await captures.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            if (failure is TimeoutException)
            {
                failures.Add(failure);
                await CancelCapturesAsync(cancellation, failures);
                CloseRedirectedStreams(process, failures);
            }
            else
            {
                failures.AddTaskFailures(captures, failure);
            }
        }
        finally
        {
            try
            {
                // Keep the aggregate task observed as well as both direct owner joins.
                await captures;
            }
            catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
            {
                failures.AddTaskFailures(captures, failure);
            }
        }
    }

    private static async Task CancelCapturesAsync(CancellationTokenSource cancellation, NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            await cancellation.CancelAsync();
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    private static void CloseRedirectedStreams(Process process, NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            process.StandardOutput.Close();
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        try
        {
            process.StandardError.Close();
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    private static async Task CaptureAsync(Stream source, Stream output, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(output, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Retain captured diagnostics while observing cancellation during child cleanup.
        }
    }

    private static void EnsureStarted(Process process)
    {
        if (!process.Start())
        {
            throw new InvalidOperationException("The real native benchmark executable did not start.");
        }
    }

    private static async Task StopAndObserveAsync(Process process, CancellationTokenSource cancellation, Task captures,
        NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            await StopAsync(process, cancellation, captures, failures);
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

}
