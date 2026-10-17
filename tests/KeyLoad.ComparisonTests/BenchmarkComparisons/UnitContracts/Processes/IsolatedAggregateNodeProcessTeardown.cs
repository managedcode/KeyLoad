using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeProcessTeardown
{
    private const string CleanupTimedOut = "The isolated aggregate Node child did not settle within its cleanup bound.";

    internal static void TryKill(Process process, IsolatedAggregateNodeFailureSet failures)
    {
        if (HasExited(process, failures))
        {
            return;
        }
        try
        {
            IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.Kill(entireProcessTree: true));
        }
        catch (AggregateException envelope)
        {
            var failure = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            if (failure is not (InvalidOperationException or Win32Exception) || !HasExited(process, failures))
            {
                failures.Add(failure);
            }
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
        var activeDeadline = deadline;
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(() => activeDeadline.CancelAsync());
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
        using var cleanup = new CancellationTokenSource(bound, TimeProvider.System);
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
                await ObserveJoinAsync(originalJoin);
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

    private static async Task ObserveJoinAsync(Task originalJoin)
    {
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => originalJoin);
        }
        catch (AggregateException)
        {
            // The original members are collected separately; this observes the same join without mirroring one cause.
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
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.HasExited);
        }
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
            await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System);
        }
    }

    private static bool HasExited(Process process, IsolatedAggregateNodeFailureSet failures)
    {
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.HasExited);
        }
        catch (AggregateException envelope)
        {
            failures.Add(IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
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
