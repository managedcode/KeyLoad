using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.CodeQuality.Processes;

internal static class ProductionSourceManifestProcessSettlement
{
    private const string SettlementFailure = "The native source-manifest producer did not settle within its cleanup bound.";

    internal static async Task JoinAsync(Process process, Task original, TimeSpan settlementTimeout,
        List<Exception> failures)
    {
        if (!original.IsCompleted)
        {
            ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
            if (!await WaitWithinDeadlineAsync(original, settlementTimeout, failures).ConfigureAwait(false))
            {
                ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
                ServerFailureObserver.Observe(process.StandardOutput.Dispose, failures);
                ServerFailureObserver.Observe(process.StandardError.Dispose, failures);
                await WaitWithinDeadlineAsync(original, settlementTimeout, failures).ConfigureAwait(false);
            }
        }
        await ObserveOriginalAsync(original, failures).ConfigureAwait(false);
    }

    private static async Task<bool> WaitWithinDeadlineAsync(Task original, TimeSpan settlementTimeout,
        List<Exception> failures)
    {
        var waiter = original.WaitAsync(settlementTimeout, TimeProvider.System);
        await waiter.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (waiter.Exception?.InnerExceptions is [TimeoutException timeout] &&
            original.Exception?.InnerExceptions.Any(failure => ReferenceEquals(failure, timeout)) != true)
        {
            failures.Add(new TimeoutException(SettlementFailure, timeout));
            return false;
        }
        return true;
    }

    private static async Task ObserveOriginalAsync(Task original, List<Exception> failures)
    {
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, observed).ConfigureAwait(false);
        var alreadyRetained = new List<Exception>(failures);
        foreach (var failure in observed)
        {
            var index = alreadyRetained.FindIndex(retained => IsSameFailure(retained, failure));
            if (index >= 0)
            {
                alreadyRetained.RemoveAt(index);
            }
            else
            {
                failures.Add(failure);
            }
        }
    }

    private static bool IsSameFailure(Exception retained, Exception failure)
        => ReferenceEquals(retained, failure) ||
            (retained is TaskCanceledException first && failure is TaskCanceledException second &&
             first.Task is not null && ReferenceEquals(first.Task, second.Task));

    private static void KillIfRunning(Process process)
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
