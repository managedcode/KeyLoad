using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimeSeriesIntensivePinnedImageReaper
{
    internal static async Task ReapAsync(Process process)
    {
        Exception? failure = null;
        try
        {
            Kill(process);
        }
        catch (Exception kill) when (TimeSeriesIntensivePinnedImageFailure.IsNative(kill))
        {
            failure = kill;
        }

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            TimeSeriesIntensivePinnedImageProtocol.OperationSeconds));
        try
        {
            await process.WaitForExitAsync(cleanup.Token);
        }
        catch (Exception wait) when (failure is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(wait))
        {
            failure ??= wait;
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Kill(Process process)
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
            // The owned native child exited during observation.
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // The operating system completed the owned child first.
        }
    }
}
