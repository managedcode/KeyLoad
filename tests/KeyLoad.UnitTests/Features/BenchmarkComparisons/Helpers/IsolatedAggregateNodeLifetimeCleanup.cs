using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeLifetimeCleanup
{
    internal static async Task CleanupAsync(Process process, Task? actualExit, Task? wait,
        Task<string>? output, Task<string>? error, CancellationTokenSource? deadline, Exception? primary,
        TimeSpan cleanupBound, IsolatedAggregateNodeFailureSet failures)
    {
        actualExit ??= IsolatedAggregateNodeProcessTeardown.RegisterExitObserver(process, failures);
        var originals = OriginalTasks(actualExit, wait, output, error);
        var terminalBeforeCleanup = originals.Select(task => task.IsCompleted).ToArray();
        await CaptureTerminalFailuresAsync(originals, terminalBeforeCleanup, primary, failures,
            deadline?.Token ?? default);
        if (primary is not null)
        {
            IsolatedAggregateNodeProcessTeardown.TryKill(process, failures);
        }
        var cancellation = IsolatedAggregateNodeProcessTeardown.CancelDeadline(deadline, failures);
        if (cancellation is not null)
        {
            originals = [.. originals, cancellation];
            terminalBeforeCleanup = [.. terminalBeforeCleanup, cancellation.IsCompleted];
        }
        var join = Task.WhenAll(originals);
        var settled = await IsolatedAggregateNodeProcessTeardown.WaitForOriginalsAsync(
            join, cleanupBound, failures);
        await FinishCleanupAsync(process, actualExit, deadline, originals, join, terminalBeforeCleanup,
            primary, settled, failures);
    }

    private static async Task FinishCleanupAsync(Process process, Task actualExit, CancellationTokenSource? deadline,
        Task[] originals,
        Task originalJoin,
        bool[] terminalBeforeCleanup, Exception? primary, bool settled,
        IsolatedAggregateNodeFailureSet failures)
    {
        if (!settled || !IsolatedAggregateNodeProcessTeardown.HasActualExit(process, actualExit, failures))
        {
            await CaptureTerminalFailuresAsync(originals, terminalBeforeCleanup, primary, failures,
                deadline?.Token ?? default);
            IsolatedAggregateNodeProcessTeardown.DeferRelease(process, deadline, originals, originalJoin, actualExit);
            return;
        }
        await CaptureTerminalFailuresAsync(originals, terminalBeforeCleanup, primary, failures,
            deadline?.Token ?? default);
        IsolatedAggregateNodeProcessTeardown.ReleaseNow(process, deadline, failures);
    }

    private static Task[] OriginalTasks(Task? actualExit, Task? wait, Task<string>? output, Task<string>? error)
        => [.. new Task?[] { actualExit, wait, output, error }.OfType<Task>()];

    internal static async Task AwaitOriginalOperationsAsync(Task wait, Task output, Task error)
    {
        var pending = new List<Task> { wait, output, error };
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            if (completed.IsFaulted || completed.IsCanceled)
            {
                await completed;
            }
        }
    }

    private static async Task CaptureTerminalFailuresAsync(Task[] originals, bool[] terminalBeforeCleanup,
        Exception? primary, IsolatedAggregateNodeFailureSet failures, CancellationToken deadline)
    {
        for (var index = 0; index < originals.Length; index++)
        {
            var task = originals[index];
            if (!task.IsCompleted)
            {
                continue;
            }
            await CaptureTaskFailureAsync(task, terminalBeforeCleanup[index], primary, failures, deadline);
        }
    }

    private static async Task CaptureTaskFailureAsync(Task task, bool wasTerminal,
        Exception? primary, IsolatedAggregateNodeFailureSet failures, CancellationToken deadline)
    {
        if (task.IsFaulted && task.Exception is { } aggregate)
        {
            foreach (var failure in aggregate.InnerExceptions)
            {
                RecordUnlessExpected(failure, wasTerminal, primary, failures, deadline);
            }
        }
        else if (task.IsCanceled)
        {
            await CaptureCancellationAsync(task, wasTerminal, primary, failures, deadline);
        }
    }

    private static async Task CaptureCancellationAsync(Task task, bool wasTerminal,
        Exception? primary, IsolatedAggregateNodeFailureSet failures, CancellationToken deadline)
    {
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => task);
        }
        catch (AggregateException envelope)
        {
            RecordUnlessExpected(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope),
                wasTerminal, primary, failures, deadline);
        }
    }

    private static void RecordUnlessExpected(Exception failure, bool wasTerminal,
        Exception? primary, IsolatedAggregateNodeFailureSet failures, CancellationToken deadline)
    {
        if (!IsExpectedCancellation(failure, wasTerminal, primary, deadline))
        {
            failures.Add(failure);
        }
    }

    private static bool IsExpectedCancellation(Exception failure, bool wasTerminal,
        Exception? primary, CancellationToken deadline)
    {
        if (wasTerminal)
        {
            // Same-token sibling cancellations are one caller cancellation, not independent faults.
            return failure is OperationCanceledException terminalCancellation
                && primary is OperationCanceledException primaryCancellation
                && terminalCancellation.CancellationToken == primaryCancellation.CancellationToken
                && terminalCancellation.CancellationToken.IsCancellationRequested;
        }
        return failure is OperationCanceledException cancellation
            && deadline.IsCancellationRequested
            && cancellation.CancellationToken == deadline;
    }

}
