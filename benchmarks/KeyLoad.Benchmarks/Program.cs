using BenchmarkDotNet.Running;
using KeyLoad;
using KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

_ = SerializationExecutionRegistration.Process.Value;
BenchmarkSwitcher.FromAssembly(typeof(EmbeddedBenchmarks).Assembly).Run(args);
