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
