using System.Diagnostics;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal static class C1OutcomeInspectionDeadline
{
    private const string CancellationMessage = "The outcome inspection process was canceled.";
    private const string ExecutionTimeoutMessage = "The outcome inspection process exceeded its execution deadline.";
    private const string CleanupTimeoutMessage = "The outcome inspection process exceeded its cleanup deadline.";

    internal static async Task WaitAsync(Process process, Task all, List<Exception> failures,
        IOptions<CrashHostExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        var settings = executionOptions.Value;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopped = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, timer.Token);
        var deadline = Task.Delay(settings.InspectionExecutionTimeout, TimeProvider.System, timer.Token);
        var first = await Task.WhenAny(all, stopped, deadline).ConfigureAwait(false);
        if (first == all)
        {
            await timer.CancelAsync().ConfigureAwait(false);
            await ObserveTimerAsync(stopped).ConfigureAwait(false);
            await ObserveTimerAsync(deadline).ConfigureAwait(false);
            return;
        }
        failures.Add(cancellationToken.IsCancellationRequested
            ? new OperationCanceledException(CancellationMessage, cancellationToken)
            : new TimeoutException(ExecutionTimeoutMessage));
        ServerFailureObserver.Observe(() => C1OutcomeInspectionProcessIo.Kill(process), failures);
        using var cleanupTimer = new CancellationTokenSource();
        var cleanup = Task.Delay(settings.InspectionCleanupTimeout, TimeProvider.System, cleanupTimer.Token);
        if (await Task.WhenAny(all, cleanup).ConfigureAwait(false) != all)
        {
            failures.Add(new TimeoutException(CleanupTimeoutMessage));
            ServerFailureObserver.Observe(() => C1OutcomeInspectionProcessIo.Kill(process), failures);
        }
        await cleanupTimer.CancelAsync().ConfigureAwait(false);
        await ObserveTimerAsync(cleanup).ConfigureAwait(false);
        await timer.CancelAsync().ConfigureAwait(false);
        await ObserveTimerAsync(stopped).ConfigureAwait(false);
        await ObserveTimerAsync(deadline).ConfigureAwait(false);
    }

    private static async Task ObserveTimerAsync(Task timer)
    {
        try
        {
            await timer.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }
}
