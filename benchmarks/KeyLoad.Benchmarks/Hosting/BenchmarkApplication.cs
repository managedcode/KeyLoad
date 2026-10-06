using KeyLoad.Benchmarks.Features.BenchmarkComparisons;

namespace KeyLoad.Benchmarks.Hosting;

/// <summary>Composes the benchmark command-line application.</summary>
internal static class BenchmarkApplication
{
    internal static void Run(string[] arguments)
    {
        _ = SerializationExecutionRegistration.Process.Value;
        BenchmarkComparisonRunner.Run(arguments);
    }
}
