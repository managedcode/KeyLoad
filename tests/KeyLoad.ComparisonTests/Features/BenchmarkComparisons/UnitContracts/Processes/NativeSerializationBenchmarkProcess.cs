using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>
/// Owns the real child and both output pumps until settlement. The 15-minute execution deadline
/// and 15-second capture diagnostic threshold do not guarantee a hard return bound for OS I/O;
/// after interrupting redirected streams, the final join retains ownership of every pump outcome.
/// </summary>
internal static class NativeSerializationBenchmarkProcess
{
    internal static readonly string[] DryArguments = NativeSerializationBenchmarkStartInfo.DryArguments;

    internal static Task<int> RunAsync(string directory, CancellationToken cancellationToken)
        => RunProcessAsync(NativeSerializationBenchmarkStartInfo.Create(directory), directory, cancellationToken);

    internal static async Task<int> RunProcessAsync(ProcessStartInfo startInfo, string directory, CancellationToken cancellationToken)
    {
        var failures = new NativeSerializationBenchmarkFailures();
        var exitCode = -1;
        try
        {
            using var process = new Process { StartInfo = startInfo };
            await using var files = new NativeSerializationBenchmarkCaptureFiles(directory, failures);
            exitCode = await files.RunAsync(process, cancellationToken);
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        failures.ThrowIfAny();
        return exitCode;
    }

}
