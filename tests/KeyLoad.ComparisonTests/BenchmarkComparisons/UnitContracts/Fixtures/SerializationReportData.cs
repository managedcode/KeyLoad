using System.Text.Json.Nodes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Builds controlled exporter-schema inputs; these are never benchmark measurements.</summary>
internal static class SerializationReportData
{
    internal const string BenchmarkNamespace = "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons";
    internal static readonly string[] Types =
        ["DocumentSerializationBenchmarks", "CommandSerializationBenchmarks", "StorageSerializationBenchmarks"];
    private static readonly string[] Methods = ["RuntimeEncode", "RuntimeDecode", "JsonEncode", "JsonDecode"];
    private static readonly int[] Sizes = [1024, 16384];
    private const int LaunchCount = 2;
    private const int WarmupCount = 3;
    private const int ActualCount = 6;
    private const int Operations = 32;

    internal static JsonArray Reports(int resultCount = ActualCount, bool zeroAllocation = true)
    {
        var reports = new JsonArray();
        foreach (var type in Types)
        {
            var benchmarks = new JsonArray();
            foreach (var size in Sizes)
            {
                foreach (var method in Methods)
                {
                    benchmarks.Add(Benchmark(type, size, method, resultCount, zeroAllocation));
                }
            }
            reports.Add(new JsonObject { [SerializationReportFields.HostEnvironmentInfo] = Host(), [SerializationReportFields.Benchmarks] = benchmarks });
        }
        return reports;
    }

    internal static JsonObject Corpus()
        => new()
        {
            [SerializationReportFields.Fixture] = Types[0],
            [SerializationReportFields.PayloadBytes] = Sizes[0],
            [SerializationReportFields.CorpusSha256] = new string('a', 64),
            [SerializationReportFields.JsonSha256] = new string('a', 64),
            [SerializationReportFields.RuntimeSha256] = new string('b', 64),
            [SerializationReportFields.RuntimeBytes] = 1120,
            [SerializationReportFields.JsonBytes] = 1240
        };

    private static JsonObject Host()
        => new()
        {
            [SerializationReportFields.BenchmarkDotNetVersion] = "0.15.8+controlled-schema",
            [SerializationReportFields.RuntimeVersion] = ".NET 10.0.0 (10.0.25.45414)",
            [SerializationReportFields.OsVersion] = "Ubuntu 24.04.3 LTS",
            [SerializationReportFields.ProcessorName] = "Controlled schema CPU",
            [SerializationReportFields.LogicalCoreCount] = 4,
            [SerializationReportFields.Architecture] = "X64",
            [SerializationReportFields.Configuration] = "RELEASE",
            [SerializationReportFields.HasAttachedDebugger] = false,
            [SerializationReportFields.HasRyuJit] = true,
            [SerializationReportFields.DotNetCliVersion] = "10.0.401"
        };

    private static JsonObject Benchmark(string type, int size, string method, int resultCount, bool zeroAllocation)
    {
        var measurements = Measurements(resultCount);
        return new()
        {
            [SerializationReportFields.Namespace] = BenchmarkNamespace,
            [SerializationReportFields.Type] = type,
            [SerializationReportFields.Method] = method,
            [SerializationReportFields.Parameters] = $"PayloadBytes={size}",
            [SerializationReportFields.DisplayInfo] = $"{type}.{method}: OrleansSerialization(Runtime=.NET 10.0, IterationCount=6, IterationTime=200ms, LaunchCount=2, WarmupCount=3) [PayloadBytes={size}]",
            [SerializationReportFields.Statistics] = Statistics(resultCount),
            [SerializationReportFields.Measurements] = measurements,
            [SerializationReportFields.Memory] = new JsonObject
            {
                [SerializationReportFields.BytesAllocatedPerOperation] = zeroAllocation ? 0 : 1200,
                [SerializationReportFields.TotalOperations] = Operations * ActualCount,
                [SerializationReportFields.Gen0Collections] = 0,
                [SerializationReportFields.Gen1Collections] = 0,
                [SerializationReportFields.Gen2Collections] = 0
            }
        };
    }

    private static JsonArray Measurements(int resultCount)
    {
        var rows = new JsonArray();
        for (var launch = 1; launch <= LaunchCount; launch++)
        {
            AddStage(rows, launch, "Warmup", WarmupCount);
            AddStage(rows, launch, "Actual", ActualCount);
            AddStage(rows, launch, "Result", resultCount);
        }
        return rows;
    }

    private static void AddStage(JsonArray rows, int launch, string stage, int count)
    {
        for (var iteration = 1; iteration <= count; iteration++)
        {
            rows.Add(new JsonObject
            {
                [SerializationReportFields.IterationMode] = "Workload",
                [SerializationReportFields.IterationStage] = stage,
                [SerializationReportFields.LaunchIndex] = launch,
                [SerializationReportFields.IterationIndex] = iteration,
                [SerializationReportFields.Operations] = Operations,
                [SerializationReportFields.Nanoseconds] = 1000d
            });
        }
    }

    private static JsonObject Statistics(int count)
    {
        var values = new JsonArray();
        for (var sample = 0; sample < count * LaunchCount; sample++)
        {
            values.Add(1000d / Operations);
        }
        return new()
        {
            [SerializationReportFields.N] = values.Count,
            [SerializationReportFields.OriginalValues] = values,
            [SerializationReportFields.Mean] = 1000d / Operations,
            [SerializationReportFields.Median] = 1000d / Operations,
            [SerializationReportFields.StandardDeviation] = 0d,
            [SerializationReportFields.StandardError] = 0d
        };
    }
}
