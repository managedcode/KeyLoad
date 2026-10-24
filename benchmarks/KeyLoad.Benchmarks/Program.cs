using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

BenchmarkSwitcher.FromAssembly(typeof(EmbeddedBenchmarks).Assembly).Run(args);
