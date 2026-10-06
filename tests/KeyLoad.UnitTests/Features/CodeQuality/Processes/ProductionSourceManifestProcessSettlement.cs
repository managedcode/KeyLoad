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
            await ServerFailureObserver.ObserveAsync(() => original.WaitAsync(settlementTimeout), failures)
                .ConfigureAwait(false);
            if (!original.IsCompleted)
            {
                failures.Add(new TimeoutException(SettlementFailure));
                ServerFailureObserver.Observe(() => KillIfRunning(process), failures);
                ServerFailureObserver.Observe(process.StandardOutput.Dispose, failures);
                ServerFailureObserver.Observe(process.StandardError.Dispose, failures);
                await ServerFailureObserver.ObserveAsync(() => original.WaitAsync(settlementTimeout), failures)
                    .ConfigureAwait(false);
                if (original.IsCompleted)
                {
                    await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
                }
            }
        }
    }

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
