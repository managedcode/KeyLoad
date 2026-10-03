using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeTestProcessReaper
{
    private const int CleanupSeconds = 5;

    internal static async Task ReapOwnedProcessesAsync(List<IsolatedAggregateNodeIdentity> identities,
        Stopwatch timer, List<Exception> failures)
    {
        foreach (var identity in identities.AsEnumerable().Reverse())
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                () => ReapOwnedProcessAsync(identity, timer, failures),
                failure => IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure));
        }
    }

    private static async Task ReapOwnedProcessAsync(IsolatedAggregateNodeIdentity identity,
        Stopwatch timer, List<Exception> failures)
    {
        var process = TryOpenProcess(identity.ProcessId, failures);
        if (process is null)
        {
            return;
        }
        var owned = false;
        var deferred = false;
        Task? exit = null;
        try
        {
            if (!MatchesIdentity(process, identity, failures))
            {
                return;
            }
            owned = true;
            exit = RegisterExit(process, failures);
            await KillAndWaitAsync(process, exit, timer, identity, failures,
                processOwner: process, transfer => deferred = transfer);
        }
        finally
        {
            FinishProcessOwnership(process, owned, deferred, exit, identity, failures);
        }
    }

    private static Process? TryOpenProcess(int processId, List<Exception> failures)
    {
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(() => Process.GetProcessById(processId));
        }
        catch (AggregateException envelope) when (
            IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope) is ArgumentException)
        {
            return null;
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return null;
        }
    }

    private static bool MatchesIdentity(Process process, IsolatedAggregateNodeIdentity identity,
        List<Exception> failures)
    {
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(() =>
                !process.HasExited && process.StartTime.ToUniversalTime().Ticks == identity.StartTimeUtcTicks);
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return false;
        }
    }

    private static Task RegisterExit(Process process, List<Exception> failures)
    {
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(
                () => process.WaitForExitAsync(CancellationToken.None));
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return PollForActualExitAsync(process);
        }
    }

    private static async Task KillAndWaitAsync(Process process, Task exit, Stopwatch timer,
        IsolatedAggregateNodeIdentity identity, List<Exception> failures, Process processOwner,
        Action<bool> deferOwnership)
    {
        IsolatedAggregateNodeGuardedInvocation.Capture(
            () => process.Kill(entireProcessTree: false),
            failure => IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure));
        var remaining = TimeSpan.FromSeconds(CleanupSeconds) - timer.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            deferOwnership(!IsExited(processOwner, failures));
            if (!exit.IsCompleted)
            {
                IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                    new TimeoutException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotSettle));
                return;
            }
            await ObserveExitAsync(exit, failures);
            return;
        }
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => exit.WaitAsync(remaining));
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            deferOwnership(!IsExited(processOwner, failures));
        }
    }

    private static async Task ObserveExitAsync(Task exit, List<Exception> failures)
        => await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => exit,
            failure => IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure));

    private static void FinishProcessOwnership(Process process, bool owned, bool deferred,
        Task? exit, IsolatedAggregateNodeIdentity identity, List<Exception> failures)
    {
        if (!owned)
        {
            DisposeProcess(process, failures);
            return;
        }
        if (deferred || !IsExited(process, failures))
        {
            DeferProcessRelease(process, exit ?? PollForActualExitAsync(process));
            return;
        }
        DisposeProcess(process, failures);
    }

    private static bool IsExited(Process process, List<Exception> failures)
    {
        try
        {
            return IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.HasExited);
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return false;
        }
    }

    private static async Task PollForActualExitAsync(Process process)
    {
        while (!IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.HasExited))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static void DeferProcessRelease(Process process, Task exit)
    {
        var owner = new IsolatedAggregateNodeDeferredProcessRelease(process, exit);
        owner.Start();
    }

    private static void DisposeProcess(Process process, List<Exception> failures)
        => IsolatedAggregateNodeGuardedInvocation.Capture(process.Dispose,
            failure => IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure));
}
