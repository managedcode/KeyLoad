using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanProcessSettlement
{
    internal static async Task ObserveAsync(Process process, bool started, Task<string>? output,
        Task<string>? error, List<Exception> failures)
    {
        if (started && !process.HasExited)
        {
            ServerFailureObserver.Observe(() => KillOwnedProcess(process), failures);
        }
        if (started)
        {
            await ServerFailureObserver.ObserveAsync(() => process.WaitForExitAsync(CancellationToken.None), failures)
                .ConfigureAwait(false);
        }
        if (output is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => output, failures).ConfigureAwait(false);
        }
        if (error is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => error, failures).ConfigureAwait(false);
        }
    }

    internal static async Task RecordObservationAsync(Process process, bool started, Task<string>? output,
        Task<string>? error, double executionMilliseconds, double settlementMilliseconds,
        bool callerCancelledAtExecutionCompletion, bool deadlineCancelledAtExecutionCompletion,
        OpenLoopPlanOutputCapture outputCapture, OpenLoopPlanOutputCapture errorCapture,
        Func<OpenLoopPlanStageObservation, Task>? observeSettlement, List<Exception> failures)
    {
        if (observeSettlement is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => observeSettlement(new(started,
                started ? process.Id : null, started && process.HasExited ? process.ExitCode : null,
                executionMilliseconds, settlementMilliseconds, callerCancelledAtExecutionCompletion,
                deadlineCancelledAtExecutionCompletion, output?.Status, error?.Status,
                outputCapture.ExceededBound, errorCapture.ExceededBound,
                output is null ? null : outputCapture.CapturedOutput,
                error is null ? null : errorCapture.CapturedOutput,
                failures.Select(static failure => failure.GetType().FullName ?? failure.GetType().Name).ToArray())),
                failures).ConfigureAwait(false);
        }
    }

    internal static async Task<OpenLoopPlanNodeResult?> CaptureResultAsync(Process process, bool started,
        Task<string>? output, Task<string>? error, List<Exception> failures)
    {
        OpenLoopPlanNodeResult? result = null;
        if (started && failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var captured = await Task.WhenAll(output!, error!).ConfigureAwait(false);
                result = new(process.ExitCode, captured[0], captured[1]);
            }, failures).ConfigureAwait(false);
        }
        return result;
    }

    private static void KillOwnedProcess(Process process)
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
        }
    }
}
