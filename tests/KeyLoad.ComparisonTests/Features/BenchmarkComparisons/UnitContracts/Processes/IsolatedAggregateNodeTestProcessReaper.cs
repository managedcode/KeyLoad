using System.ComponentModel;
using System.Diagnostics;
using KeyLoad.UnitTests.Features.TestInfrastructure;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeTestProcessReaper
{
    private const int CleanupSeconds = 5;

    internal static async Task ReapOwnedProcessesAsync(List<IsolatedAggregateNodeIdentity> identities,
        TestElapsedClock timer, List<Exception> failures)
    {
        foreach (var identity in identities.AsEnumerable().Reverse())
        {
            await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
                () => ReapOwnedProcessAsync(identity, timer, failures),
                failure => IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure));
        }
    }

    private static async Task ReapOwnedProcessAsync(IsolatedAggregateNodeIdentity identity,
        TestElapsedClock timer, List<Exception> failures)
    {
        var process = TryOpenProcess(identity.ProcessId, failures);
        if (process is null)
        {
            return;
        }
        var owned = false;
        Task? exit = null;
        try
        {
            if (!MatchesIdentity(process, identity, failures))
            {
                return;
            }
            owned = true;
            exit = RegisterExit(process, failures);
            var deferred = await KillAndWaitAsync(process, exit, timer, failures);
            await FinishProcessOwnershipAsync(process, owned, deferred, exit, failures);
            process = null;
        }
        finally
        {
            if (process is not null)
            {
                await FinishProcessOwnershipAsync(process, owned, false, exit, failures);
            }
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
                IsolatedAggregateNodeIdentityObservation.IsSameProcessRunning(process, identity.StartTimeUtcTicks));
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

    private static async Task<bool> KillAndWaitAsync(Process process, Task exit, TestElapsedClock timer,
        List<Exception> failures)
    {
        KillOwnedProcess(process, failures);
        var remaining = TimeSpan.FromSeconds(CleanupSeconds) - timer.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            if (exit.IsCompleted && IsExited(process, failures))
            {
                await ObserveExitAsync(exit, failures);
                return false;
            }
            if (!exit.IsCompleted || !IsExited(process, failures))
            {
                IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                    new TimeoutException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotSettle));
                return true;
            }
            await ObserveExitAsync(exit, failures);
            return !exit.IsCompletedSuccessfully || !IsExited(process, failures);
        }
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => exit.WaitAsync(remaining, timer.Provider));
            return !exit.IsCompleted || !IsExited(process, failures);
        }
        catch (AggregateException envelope)
        {
            IsolatedAggregateNodeTestCleanup.AddDistinct(failures,
                IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
            return !exit.IsCompletedSuccessfully || !IsExited(process, failures);
        }
    }

    private static async Task ObserveExitAsync(Task exit, List<Exception> failures)
        => await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => exit,
            failure => IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure));

    private static void KillOwnedProcess(Process process, List<Exception> failures)
    {
        try
        {
            IsolatedAggregateNodeGuardedInvocation.Invoke(() => process.Kill(entireProcessTree: false));
        }
        catch (AggregateException envelope)
        {
            var failure = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            if (failure is not (InvalidOperationException or Win32Exception) || !IsExited(process, failures))
            {
                IsolatedAggregateNodeTestCleanup.AddDistinct(failures, failure);
            }
        }
    }

    private static async Task FinishProcessOwnershipAsync(Process process, bool owned, bool deferred,
        Task? exit, List<Exception> failures)
    {
        if (!owned)
        {
            DisposeProcess(process, failures);
            return;
        }
        if (deferred || exit is not { IsCompletedSuccessfully: true } || !IsExited(process, failures))
        {
            DeferProcessRelease(process, exit ?? PollForActualExitAsync(process));
            return;
        }
        await ObserveExitAsync(exit, failures);
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
        while (!process.HasExited)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System);
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
