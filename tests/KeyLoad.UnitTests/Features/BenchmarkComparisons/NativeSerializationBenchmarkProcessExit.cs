using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Records termination diagnostics while the capture owner retains the original exit task.</summary>
internal static class NativeSerializationBenchmarkProcessExit
{
    internal static async Task ObserveDiagnosticAsync(Process process, Task exit, NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            KillProcessTree(process);
            await exit.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
            RetryKill(process, failures);
        }
    }

    internal static void RetryKill(Process process, NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            KillProcessTree(process);
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    private static void KillProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // Exit raced termination; the original exit task remains owned by Captures.
        }
    }
}
