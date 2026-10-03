using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeLifetime
{
    internal static async Task<IsolatedAggregateNodeResult> RunAsync(Process process, CancellationToken prompt,
        TimeSpan runBound, TimeSpan cleanupBound)
    {
        var failures = new IsolatedAggregateNodeFailureSet();
        CancellationTokenSource? deadline = null;
        Task? actualExit = null;
        Task? wait = null;
        Task<string>? output = null;
        Task<string>? error = null;
        IsolatedAggregateNodeResult? result = null;
        Exception? primary = null;
        try
        {
            actualExit = process.WaitForExitAsync(CancellationToken.None);
            deadline = CancellationTokenSource.CreateLinkedTokenSource(prompt);
            deadline.CancelAfter(runBound);
            output = IsolatedAggregateNodeOutput.ReadAsync(process.StandardOutput, deadline.Token);
            error = IsolatedAggregateNodeOutput.ReadAsync(process.StandardError, deadline.Token);
            wait = process.WaitForExitAsync(deadline.Token);
            await IsolatedAggregateNodeLifetimeCleanup.AwaitOriginalOperationsAsync(wait, output, error);
            result = new(process.ExitCode, await output, await error);
        }
        catch (Exception failure)
        {
            primary = failure;
        }

        await IsolatedAggregateNodeLifetimeCleanup.CleanupAsync(process, actualExit, wait, output, error,
            deadline, primary, cleanupBound, failures);
        failures.Throw(primary);
        return result ?? throw new InvalidOperationException(IsolatedAggregateNodeProcess.StartFailure);
    }
}
