using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeProcessTeardown
{
    private const string CleanupTimedOut = "The isolated aggregate Node child did not settle within its cleanup bound.";

    internal static void TryKill(Process process, IsolatedAggregateNodeFailureSet failures)
    {
        try
        {
            IsolatedAggregateNodeGuardedInvocation.Invoke(() =>
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            });
        }
        catch (InvalidOperationException) when (HasExited(process))
        {
        }
        catch (Win32Exception) when (HasExited(process))
        {
        }
        catch (AggregateException envelope)
        {
            failures.Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
        }
    }

    internal static Task RegisterExitObserver(Process process, IsolatedAggregateNodeFailureSet failures)
    {
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(
                () => process.WaitForExitAsync(CancellationToken.None));
        }
        catch (AggregateException envelope)
        {
            failures.Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return PollForActualExitAsync(process);
        }
    }

    internal static Task? CancelDeadline(CancellationTokenSource? deadline,
        IsolatedAggregateNodeFailureSet failures)
    {
        if (deadline is null)
        {
            return null;
        }
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(() => deadline.CancelAsync());
        }
        catch (AggregateException envelope)
        {
            failures.Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return null;
        }
    }

    internal static async Task<bool> WaitForOriginalsAsync(Task originalJoin, TimeSpan bound,
        IsolatedAggregateNodeFailureSet failures)
    {
        using var cleanup = new CancellationTokenSource(bound);
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => originalJoin.WaitAsync(cleanup.Token));
            return true;
        }
        catch (AggregateException envelope)
        {
            var observed = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            if (originalJoin.IsCompleted)
            {
                await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                    () => originalJoin, failures.Add);
                return true;
            }
            if (observed is OperationCanceledException && cleanup.IsCancellationRequested)
            {
                failures.Add(new TimeoutException(CleanupTimedOut));
                return false;
            }
            failures.Add(observed);
            return false;
        }
    }

    internal static void ReleaseNow(Process process, CancellationTokenSource? deadline,
        IsolatedAggregateNodeFailureSet failures)
    {
        DisposeDeadline(deadline, failures);
        IsolatedAggregateNodeGuardedInvocation.Capture(process.Dispose, failures.Add);
    }

    internal static void DeferRelease(Process process, CancellationTokenSource? deadline, Task[] originals,
        Task originalJoin, Task actualExit)
    {
        var owner = new IsolatedAggregateNodeDeferredRelease(process, deadline, originals, originalJoin, actualExit);
        owner.Start();
    }

    internal static bool HasActualExit(Process process, Task actualExit,
        IsolatedAggregateNodeFailureSet failures)
    {
        if (!actualExit.IsCompletedSuccessfully)
        {
            return false;
        }
        try { return IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.HasExited); }
        catch (AggregateException envelope)
        {
            failures.Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return false;
        }
    }

    private static async Task PollForActualExitAsync(Process process)
    {
        while (!process.HasExited)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void DisposeDeadline(CancellationTokenSource? deadline,
        IsolatedAggregateNodeFailureSet failures)
    {
        if (deadline is not null)
        {
            IsolatedAggregateNodeGuardedInvocation.Capture(deadline.Dispose, failures.Add);
        }
    }
}
