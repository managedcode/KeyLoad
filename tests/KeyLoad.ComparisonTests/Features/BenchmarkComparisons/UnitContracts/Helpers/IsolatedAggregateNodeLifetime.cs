using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeLifetime
{
    internal static async Task<IsolatedAggregateNodeResult> RunAsync(Process process,
        TimeSpan runBound, TimeSpan cleanupBound, CancellationToken prompt)
    {
        var failures = new IsolatedAggregateNodeFailureSet();
        using var deadlineOwner = new IsolatedAggregateNodeDeadlineOwner(failures);
        Task? actualExit = null;
        Task? wait = null;
        Task<string>? output = null;
        Task<string>? error = null;
        IsolatedAggregateNodeResult? result = null;
        Exception? primary = null;
        try
        {
            actualExit = IsolatedAggregateNodeGuardedInvocation.Invoke(
                () => process.WaitForExitAsync(CancellationToken.None));
            var deadlineToken = IsolatedAggregateNodeGuardedInvocation.Invoke(
                () => deadlineOwner.Start(runBound, prompt));
            var activeOutput = IsolatedAggregateNodeGuardedInvocation.Invoke(
                () => IsolatedAggregateNodeOutput.ReadAsync(process.StandardOutput, deadlineToken));
            output = activeOutput;
            var activeError = IsolatedAggregateNodeGuardedInvocation.Invoke(
                () => IsolatedAggregateNodeOutput.ReadAsync(process.StandardError, deadlineToken));
            error = activeError;
            var activeWait = IsolatedAggregateNodeGuardedInvocation.Invoke(
                () => process.WaitForExitAsync(deadlineToken));
            wait = activeWait;
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                () => IsolatedAggregateNodeLifetimeCleanup.AwaitOriginalOperationsAsync(
                    activeWait, activeOutput, activeError));
            result = CreateResult(process, activeOutput, activeError);
        }
        catch (AggregateException envelope)
        {
            primary = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
        }
        var cleanupDeadline = deadlineOwner.TransferToCleanup();
        await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
            () => IsolatedAggregateNodeLifetimeCleanup.CleanupAsync(process, actualExit, wait, output, error,
                cleanupDeadline, primary, cleanupBound, failures), failures.Add);
        failures.Throw(primary);
        return result ?? throw new InvalidOperationException(IsolatedAggregateNodeProcess.StartFailure);
    }

    private static IsolatedAggregateNodeResult CreateResult(Process process, Task<string> output, Task<string> error)
    {
        return IsolatedAggregateNodeGuardedInvocation.Invoke(
            () => new IsolatedAggregateNodeResult(process.ExitCode, output.GetAwaiter().GetResult(),
                error.GetAwaiter().GetResult()));
    }

}
