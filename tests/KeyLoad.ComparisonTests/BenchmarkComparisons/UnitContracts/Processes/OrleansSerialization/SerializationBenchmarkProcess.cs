using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>
/// Owns the real child and both output pumps until settlement. The 15-minute execution deadline
/// and 15-second capture diagnostic threshold do not guarantee a hard return bound for OS I/O;
/// after interrupting redirected streams, the final join retains ownership of every pump outcome.
/// </summary>
internal static class SerializationBenchmarkProcess
{
    internal static readonly string[] DryArguments = SerializationBenchmarkStartInfo.DryArguments;

    internal static Task<int> RunAsync(string directory, CancellationToken cancellationToken)
        => RunProcessAsync(SerializationBenchmarkStartInfo.Create(directory), directory, cancellationToken);

    internal static async Task<int> RunProcessAsync(ProcessStartInfo startInfo, string directory, CancellationToken cancellationToken)
    {
        var failures = new SerializationBenchmarkFailures();
        var exitCode = -1;
        try
        {
            using var process = new Process { StartInfo = startInfo };
            await using var files = new SerializationBenchmarkCaptureFiles(directory, failures);
            exitCode = await files.RunAsync(process, cancellationToken);
        }
        catch (Exception failure) when (SerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        failures.ThrowIfAny();
        return exitCode;
    }

}
