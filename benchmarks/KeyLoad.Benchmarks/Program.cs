using BenchmarkDotNet.Running;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

BenchmarkSwitcher.FromAssembly(typeof(EmbeddedBenchmarks).Assembly).Run(args);
