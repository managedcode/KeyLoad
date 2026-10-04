using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodePipeFailureOwner : IAsyncDisposable
{
    private const string ProcessReleaseFailure = "Native Node child process release failed.";
    private readonly CancellationToken testToken;
    private readonly IsolatedAggregateNodeFailureSet failures = new();
    private readonly IsolatedAggregateNodeDeadlineOwner deadlineOwner;
    private Process? process;
    private Task? actualExit;
    private Task? wait;
    private Task<string>? output;
    private Task<string>? error;
    private InvalidOperationException? outputFailure;
    private InvalidOperationException? errorFailure;
    private bool started;
    private bool cleanupAttempted;

    internal IsolatedAggregateNodePipeFailureOwner(CancellationToken testToken)
    {
        this.testToken = testToken;
        deadlineOwner = new IsolatedAggregateNodeDeadlineOwner(failures);
        var startInfo = IsolatedAggregateNodeProcess.StartInfo(
            ["-e", IsolatedAggregateNodeLifetimeProgram.Source, IsolatedAggregateNodeLifetimeProgram.BothOutputLimits]);
        process = new Process { StartInfo = startInfo };
    }

    internal async Task<IsolatedAggregateNodePipeFailures> CaptureAsync()
    {
        Exception? primary = null;
        IsolatedAggregateNodePipeFailures? result = null;
        try
        {
            result = await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(CaptureReaderFailuresAsync);
        }
        catch (AggregateException envelope)
        {
            primary = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
        }
        finally
        {
            cleanupAttempted = true;
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                () => CleanupIfOwnedAsync(primary), failures.Add);
        }
        failures.ThrowExcepting(primary, ExpectedReaderFailures());
        return result ?? throw new InvalidOperationException(
            IsolatedAggregateNodePipeFailureOwnerEvidence.MissingAggregate);
    }

    public async ValueTask DisposeAsync()
    {
        if (cleanupAttempted)
        {
            deadlineOwner.Dispose();
            return;
        }
        cleanupAttempted = true;
        if (started)
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                () => CleanupStartedProcessAsync(primary: null), failures.Add);
        }
        else
        {
            try
            {
                DisposeUnstartedProcess();
            }
            catch (AggregateException envelope)
            {
                failures.Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            }
        }
        deadlineOwner.Dispose();
        failures.Throw(primary: null);
    }

    private async Task<IsolatedAggregateNodePipeFailures> CaptureReaderFailuresAsync()
    {
        StartProcess();
        RegisterOriginalOperations();
        outputFailure = await IsolatedAggregateNodePipeFailureOwnerEvidence.CaptureLimitFailureAsync(output!);
        errorFailure = await IsolatedAggregateNodePipeFailureOwnerEvidence.CaptureLimitFailureAsync(error!);
        await CleanupStartedProcessAsync(outputFailure);
        var combined = IsolatedAggregateNodePipeFailureOwnerEvidence.CaptureCombinedFailures(failures);
        return new(outputFailure, errorFailure, combined);
    }

    private void StartProcess()
    {
        if (!process!.Start())
        {
            throw new InvalidOperationException(IsolatedAggregateNodeProcess.StartFailure);
        }
        started = true;
    }

    private void RegisterOriginalOperations()
    {
        actualExit = IsolatedAggregateNodePipeFailureOwnerEvidence.InvokeNative(
            () => process!.WaitForExitAsync(CancellationToken.None));
        var runToken = deadlineOwner.Start(
            TimeSpan.FromSeconds(IsolatedAggregateNodeProcess.TimeoutSeconds), testToken);
        var activeOutput = IsolatedAggregateNodePipeFailureOwnerEvidence.InvokeNative(
            () => IsolatedAggregateNodeOutput.ReadAsync(process!.StandardOutput, runToken));
        output = activeOutput;
        var activeError = IsolatedAggregateNodePipeFailureOwnerEvidence.InvokeNative(
            () => IsolatedAggregateNodeOutput.ReadAsync(process!.StandardError, runToken));
        error = activeError;
        var activeWait = IsolatedAggregateNodePipeFailureOwnerEvidence.InvokeNative(
            () => process!.WaitForExitAsync(runToken));
        wait = activeWait;
    }

    private async Task CleanupStartedProcessAsync(Exception? primary)
    {
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                () => IsolatedAggregateNodeLifetimeCleanup.CleanupAsync(process!, actualExit, wait,
                    output, error, deadlineOwner.TransferToCleanup(), primary,
                    TimeSpan.FromSeconds(IsolatedAggregateNodeProcess.CleanupSeconds), failures), failures.Add);
        }
        finally
        {
            process = null;
            deadlineOwner.Dispose();
        }
    }

    private async Task CleanupIfOwnedAsync(Exception? primary)
    {
        if (process is null)
        {
            return;
        }
        if (started)
        {
            await CleanupStartedProcessAsync(primary);
            return;
        }
        try
        {
            DisposeUnstartedProcess();
        }
        catch (AggregateException envelope)
        {
            failures.Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
        }
    }

    private void DisposeUnstartedProcess()
    {
        try
        {
            process?.Dispose();
        }
        catch (Exception failure)
        {
            throw new AggregateException(ProcessReleaseFailure, failure);
        }
        finally
        {
            process = null;
        }
    }

    private Exception[] ExpectedReaderFailures()
        => [.. new Exception?[] { outputFailure, errorFailure }.OfType<Exception>()];
}
