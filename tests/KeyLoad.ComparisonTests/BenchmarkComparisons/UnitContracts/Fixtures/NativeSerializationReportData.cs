using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Builds controlled exporter-schema inputs; these are never benchmark measurements.</summary>
internal static class NativeSerializationReportData
{
    internal const string BenchmarkNamespace = "KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons";
    internal static readonly string[] Types =
        ["NativeDocumentSerializationBenchmarks", "NativeCommandSerializationBenchmarks", "NativeStorageSerializationBenchmarks"];
    private static readonly string[] Methods = ["NativeEncode", "NativeDecode", "JsonEncode", "JsonDecode"];
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
            reports.Add(new JsonObject { [NativeSerializationReportFields.HostEnvironmentInfo] = Host(), [NativeSerializationReportFields.Benchmarks] = benchmarks });
        }
        return reports;
    }

    internal static JsonObject Corpus()
        => new()
        {
            [NativeSerializationReportFields.Fixture] = Types[0],
            [NativeSerializationReportFields.PayloadBytes] = Sizes[0],
            [NativeSerializationReportFields.CorpusSha256] = new string('a', 64),
            [NativeSerializationReportFields.JsonSha256] = new string('a', 64),
            [NativeSerializationReportFields.NativeSha256] = new string('b', 64),
            [NativeSerializationReportFields.NativeBytes] = 1120,
            [NativeSerializationReportFields.JsonBytes] = 1240
        };

    private static JsonObject Host()
        => new()
        {
            [NativeSerializationReportFields.BenchmarkDotNetVersion] = "0.15.8+controlled-schema",
            [NativeSerializationReportFields.RuntimeVersion] = ".NET 10.0.0 (10.0.25.45414)",
            [NativeSerializationReportFields.OsVersion] = "Ubuntu 24.04.3 LTS",
            [NativeSerializationReportFields.ProcessorName] = "Controlled schema CPU",
            [NativeSerializationReportFields.LogicalCoreCount] = 4,
            [NativeSerializationReportFields.Architecture] = "X64",
            [NativeSerializationReportFields.Configuration] = "RELEASE",
            [NativeSerializationReportFields.HasAttachedDebugger] = false,
            [NativeSerializationReportFields.HasRyuJit] = true,
            [NativeSerializationReportFields.DotNetCliVersion] = "10.0.401"
        };

    private static JsonObject Benchmark(string type, int size, string method, int resultCount, bool zeroAllocation)
    {
        var measurements = Measurements(resultCount);
        return new()
        {
            [NativeSerializationReportFields.Namespace] = BenchmarkNamespace,
            [NativeSerializationReportFields.Type] = type,
            [NativeSerializationReportFields.Method] = method,
            [NativeSerializationReportFields.Parameters] = $"PayloadBytes={size}",
            [NativeSerializationReportFields.DisplayInfo] = $"{type}.{method}: NativeSerialization(Runtime=.NET 10.0, IterationCount=6, IterationTime=200ms, LaunchCount=2, WarmupCount=3) [PayloadBytes={size}]",
            [NativeSerializationReportFields.Statistics] = Statistics(resultCount),
            [NativeSerializationReportFields.Measurements] = measurements,
            [NativeSerializationReportFields.Memory] = new JsonObject
            {
                [NativeSerializationReportFields.BytesAllocatedPerOperation] = zeroAllocation ? 0 : 1200,
                [NativeSerializationReportFields.TotalOperations] = Operations * ActualCount,
                [NativeSerializationReportFields.Gen0Collections] = 0,
                [NativeSerializationReportFields.Gen1Collections] = 0,
                [NativeSerializationReportFields.Gen2Collections] = 0
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
                [NativeSerializationReportFields.IterationMode] = "Workload",
                [NativeSerializationReportFields.IterationStage] = stage,
                [NativeSerializationReportFields.LaunchIndex] = launch,
                [NativeSerializationReportFields.IterationIndex] = iteration,
                [NativeSerializationReportFields.Operations] = Operations,
                [NativeSerializationReportFields.Nanoseconds] = 1000d
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
            [NativeSerializationReportFields.N] = values.Count,
            [NativeSerializationReportFields.OriginalValues] = values,
            [NativeSerializationReportFields.Mean] = 1000d / Operations,
            [NativeSerializationReportFields.Median] = 1000d / Operations,
            [NativeSerializationReportFields.StandardDeviation] = 0d,
            [NativeSerializationReportFields.StandardError] = 0d
        };
    }
}
