using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class RawStorageReportData
{
    internal const string HostEnvironmentInfoKey = "HostEnvironmentInfo";
    internal const string BenchmarksKey = "Benchmarks";
    internal const string BenchmarkDotNetVersionKey = "BenchmarkDotNetVersion";
    internal const string RuntimeVersionKey = "RuntimeVersion";
    internal const string NamespaceKey = "Namespace";
    internal const string TypeKey = "Type";
    internal const string MethodKey = "Method";
    internal const string ParametersKey = "Parameters";
    internal const string StatisticsKey = "Statistics";
    internal const string MemoryKey = "Memory";
    internal const string MeasurementsKey = "Measurements";
    internal const string SampleCountKey = "N";
    internal const string OriginalValuesKey = "OriginalValues";
    internal const string MeanKey = "Mean";
    internal const string MedianKey = "Median";
    internal const string BytesAllocatedPerOperationKey = "BytesAllocatedPerOperation";
    internal const string TotalOperationsKey = "TotalOperations";
    internal const string IterationModeKey = "IterationMode";
    internal const string IterationStageKey = "IterationStage";
    internal const string LaunchIndexKey = "LaunchIndex";
    internal const string IterationIndexKey = "IterationIndex";
    internal const string OperationsKey = "Operations";
    internal const string NanosecondsKey = "Nanoseconds";
    internal const string Engine = "zonetree";
    internal const string BenchmarkNamespace = "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons";
    internal const string BenchmarkType = "RawStorageBenchmarks";
    internal const int RecordCount = 4096;
    private const int OutlierReducedSampleCount = 3;
    private const int RawActualCount = 5;
    private const int ActualIterationStart = 1;
    private const int ResultIterationStart = 10;
    private const string BenchmarkVersion = "0.15.8+metadata";
    internal const string PlainRuntimeVersion = ".NET 10.0.0";
    internal const string DetailedRuntimeVersion = ".NET 10.0.0 (10.0.25.45414)";
    private static readonly string[] Methods = ["PointRead", "MissingRead", "Overwrite", "CreateDelete"];
    private static readonly int[] PayloadSizes = [32, 1024];

    internal static JsonObject CreateValidReport(string engine = Engine,
        int sampleCount = OutlierReducedSampleCount, string runtimeVersion = DetailedRuntimeVersion)
    {
        var benchmarks = new JsonArray();
        foreach (var method in Methods)
        {
            foreach (var payloadBytes in PayloadSizes)
            {
                benchmarks.Add(CreateBenchmark(method, payloadBytes, engine, sampleCount));
            }
        }

        return new JsonObject
        {
            [HostEnvironmentInfoKey] = new JsonObject
            {
                [BenchmarkDotNetVersionKey] = BenchmarkVersion,
                [RuntimeVersionKey] = runtimeVersion
            },
            [BenchmarksKey] = benchmarks
        };
    }

    private static JsonObject CreateBenchmark(string method, int payloadBytes, string engine, int sampleCount)
    {
        var measurements = CreateMeasurements(sampleCount);
        measurements.Add(new JsonObject
        {
            [IterationModeKey] = "Overhead",
            [IterationStageKey] = "Warmup",
            [LaunchIndexKey] = 0,
            [IterationIndexKey] = 0,
            [OperationsKey] = 1,
            [NanosecondsKey] = 1.0
        });
        return new JsonObject
        {
            [NamespaceKey] = BenchmarkNamespace,
            [TypeKey] = BenchmarkType,
            [MethodKey] = method,
            [ParametersKey] = $"RecordCount={RecordCount}&PayloadBytes={payloadBytes}&Engine={engine}",
            [StatisticsKey] = CreateStatistics(sampleCount),
            [MemoryKey] = new JsonObject
            {
                [BytesAllocatedPerOperationKey] = 0,
                [TotalOperationsKey] = 5
            },
            [MeasurementsKey] = measurements
        };
    }

    private static JsonArray CreateMeasurements(int retainedResultCount)
    {
        var measurements = new JsonArray();
        AddStageMeasurements(measurements, "Actual", RawActualCount, ActualIterationStart);
        AddStageMeasurements(measurements, "Result", retainedResultCount, ResultIterationStart);
        return measurements;
    }

    private static void AddStageMeasurements(JsonArray measurements, string stage, int count, int iterationStart)
    {
        for (var iteration = 0; iteration < count; iteration++)
        {
            measurements.Add(new JsonObject
            {
                [IterationModeKey] = "Workload",
                [IterationStageKey] = stage,
                [LaunchIndexKey] = 0,
                [IterationIndexKey] = iterationStart + iteration,
                [OperationsKey] = 1024,
                [NanosecondsKey] = 100.0 + iteration
            });
        }
    }

    private static JsonObject CreateStatistics(int sampleCount)
    {
        var originalValues = new JsonArray();
        for (var sample = 0; sample < sampleCount; sample++)
        {
            originalValues.Add(100.0 + sample);
        }

        var meanAndMedian = sampleCount switch
        {
            1 => 100.0,
            3 => 101.0,
            _ => 102.0
        };
        return new JsonObject
        {
            [SampleCountKey] = sampleCount,
            [OriginalValuesKey] = originalValues,
            [MeanKey] = meanAndMedian,
            [MedianKey] = meanAndMedian
        };
    }
}
