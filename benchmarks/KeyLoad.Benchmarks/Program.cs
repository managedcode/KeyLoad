using KeyLoad;
using KeyLoad.Benchmarks.Features.BenchmarkComparisons;

_ = SerializationExecutionRegistration.Process.Value;
BenchmarkComparisonRunner.Run(args);
