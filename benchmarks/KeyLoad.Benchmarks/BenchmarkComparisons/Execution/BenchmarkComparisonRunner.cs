using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

namespace KeyLoad.Benchmarks.Features.BenchmarkComparisons;

/// <summary>Runs the existing BenchmarkDotNet switcher against the public scenario library.</summary>
internal static class BenchmarkComparisonRunner
{
    internal static void Run(string[] arguments)
        => BenchmarkSwitcher.FromAssembly(typeof(EmbeddedBenchmarks).Assembly).Run(arguments);
}
