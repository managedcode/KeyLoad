using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodePipeFailureFixture
{
    private const string MissingOutputFailure = "The real Node pipe did not exceed its configured output bound.";
    private const string MissingAggregate = "The owning cleanup path did not retain both actual pipe failures.";

    internal static async Task<IsolatedAggregateNodePipeFailures> CaptureBothAsync(CancellationToken testToken)
    {
        var startInfo = IsolatedAggregateNodeProcess.StartInfo(
            ["-e", IsolatedAggregateNodeLifetimeProgram.Source, IsolatedAggregateNodeLifetimeProgram.BothOutputLimits]);
        var failures = new IsolatedAggregateNodeFailureSet();
        var process = new Process { StartInfo = startInfo };
        CancellationTokenSource? deadline = null;
        Task? actualExit = null;
        Task? wait = null;
        Task<string>? output = null;
        Task<string>? error = null;
        Exception? primary = null;
        var started = false;
        var cleanupStarted = false;
        InvalidOperationException? outputFailure = null;
        InvalidOperationException? errorFailure = null;
        AggregateException? combined = null;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(IsolatedAggregateNodeProcess.StartFailure);
            }
            started = true;
            actualExit = process.WaitForExitAsync(CancellationToken.None);
            deadline = CancellationTokenSource.CreateLinkedTokenSource(testToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(IsolatedAggregateNodeProcess.TimeoutSeconds));
            output = IsolatedAggregateNodeOutput.ReadAsync(process.StandardOutput, deadline.Token);
            error = IsolatedAggregateNodeOutput.ReadAsync(process.StandardError, deadline.Token);
            wait = process.WaitForExitAsync(deadline.Token);
            outputFailure = await CaptureLimitFailureAsync(output);
            errorFailure = await CaptureLimitFailureAsync(error);
            await Task.WhenAll(actualExit, wait);
            cleanupStarted = true;
            await IsolatedAggregateNodeLifetimeCleanup.CleanupAsync(process, actualExit, wait, output, error,
                deadline, primary: null, TimeSpan.FromSeconds(IsolatedAggregateNodeProcess.CleanupSeconds), failures);
            deadline = null;
            combined = CaptureCombinedFailures(failures);
        }
        catch (Exception failure)
        {
            primary = failure;
        }
        finally
        {
            if (started && !cleanupStarted)
            {
                cleanupStarted = true;
                await TryCleanupAsync(process, actualExit, wait, output, error, deadline, primary, failures);
                deadline = null;
            }
            else if (!started)
            {
                DisposeUnstartedProcess(process, failures);
            }
        }
        failures.ThrowExcepting(primary,
            [.. new Exception?[] { outputFailure, errorFailure }.OfType<Exception>()]);
        return new(outputFailure ?? throw new InvalidOperationException(MissingOutputFailure),
            errorFailure ?? throw new InvalidOperationException(MissingOutputFailure), combined
            ?? throw new InvalidOperationException(MissingAggregate));
    }

    private static async Task<InvalidOperationException> CaptureLimitFailureAsync(Task<string> output)
    {
        try
        {
            await output;
        }
        catch (InvalidOperationException failure) when (
            failure.Message == IsolatedAggregateNodeLifetimeProgram.OutputLimitMessage)
        {
            return failure;
        }
        throw new InvalidOperationException(MissingOutputFailure);
    }

    private static AggregateException CaptureCombinedFailures(IsolatedAggregateNodeFailureSet failures)
    {
        try
        {
            failures.Throw(primary: null);
        }
        catch (AggregateException failure)
        {
            return failure;
        }
        throw new InvalidOperationException(MissingAggregate);
    }

    private static async Task TryCleanupAsync(Process process, Task? actualExit, Task? wait,
        Task<string>? output, Task<string>? error, CancellationTokenSource? deadline, Exception? primary,
        IsolatedAggregateNodeFailureSet failures)
    {
        try
        {
            await IsolatedAggregateNodeLifetimeCleanup.CleanupAsync(process, actualExit, wait, output, error,
                deadline, primary, TimeSpan.FromSeconds(IsolatedAggregateNodeProcess.CleanupSeconds), failures);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    private static void DisposeUnstartedProcess(Process process, IsolatedAggregateNodeFailureSet failures)
    {
        try
        {
            process.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }
}

internal sealed record IsolatedAggregateNodePipeFailures(
    InvalidOperationException StandardOutput, InvalidOperationException StandardError, AggregateException Combined);
