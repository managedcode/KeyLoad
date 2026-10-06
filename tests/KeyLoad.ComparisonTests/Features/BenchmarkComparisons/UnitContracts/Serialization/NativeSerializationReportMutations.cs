using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class NativeSerializationReportMutations
{
    internal static void Apply(JsonArray reports, string fault)
    {
        var benchmarks = reports[0]![NativeSerializationReportFields.Benchmarks]!.AsArray();
        var first = benchmarks[0]!;
        if (ApplyIdentityAndSettings(reports, benchmarks, first, fault)
            || ApplyStatisticsAndMemory(first, fault)
            || ApplyHost(reports, fault))
        {
            return;
        }
        Measurement(first[NativeSerializationReportFields.Measurements]!.AsArray(), fault);
    }

    private static bool ApplyIdentityAndSettings(JsonArray reports, JsonArray benchmarks, JsonNode first, string fault)
    {
        switch (fault)
        {
            case "missing-report":
                reports.RemoveAt(2);
                return true;
            case "missing-cell":
                benchmarks.RemoveAt(7);
                return true;
            case "duplicate-cell":
                benchmarks[7] = first.DeepClone();
                return true;
            case "wrong-type":
                first[NativeSerializationReportFields.Type] = "OtherBenchmarks";
                return true;
            case "wrong-method":
                first[NativeSerializationReportFields.Method] = "OtherMethod";
                return true;
            case "wrong-size":
                first[NativeSerializationReportFields.Parameters] = "PayloadBytes=2048";
                return true;
            case "wrong-settings":
                first[NativeSerializationReportFields.DisplayInfo] = first[NativeSerializationReportFields.DisplayInfo]!.GetValue<string>().Replace("LaunchCount=2", "LaunchCount=1", StringComparison.Ordinal);
                return true;
            case "wrong-runtime-setting":
                ChangeDisplay(first, "Runtime=.NET 10.0", "Runtime=.NET 9.0");
                return true;
            case "in-process-setting":
                ChangeDisplay(first, "Runtime=.NET 10.0", "Runtime=.NET 10.0, Toolchain=InProcess");
                return true;
            case "duplicate-parameter":
                first[NativeSerializationReportFields.Parameters] = "PayloadBytes=1024&PayloadBytes=1024";
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyStatisticsAndMemory(JsonNode first, string fault)
    {
        switch (fault)
        {
            case "null-statistics":
                first[NativeSerializationReportFields.Statistics] = null;
                return true;
            case "wrong-n":
                first[NativeSerializationReportFields.Statistics]![NativeSerializationReportFields.N] = 11;
                return true;
            case "wrong-original-values":
                first[NativeSerializationReportFields.Statistics]![NativeSerializationReportFields.OriginalValues]!.AsArray()[0] = 1d;
                return true;
            case "negative-deviation":
                first[NativeSerializationReportFields.Statistics]![NativeSerializationReportFields.StandardDeviation] = -1d;
                return true;
            case "negative-allocation":
                first[NativeSerializationReportFields.Memory]![NativeSerializationReportFields.BytesAllocatedPerOperation] = -1;
                return true;
            case "missing-memory":
                first.AsObject().Remove(NativeSerializationReportFields.Memory);
                return true;
            case "failed-generated-child":
                FailedChild(first);
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyHost(JsonArray reports, string fault)
    {
        switch (fault)
        {
            case "wrong-bdn-version":
                reports[0]![NativeSerializationReportFields.HostEnvironmentInfo]![NativeSerializationReportFields.BenchmarkDotNetVersion] = "0.15.7";
                return true;
            case "wrong-runtime":
                reports[0]![NativeSerializationReportFields.HostEnvironmentInfo]![NativeSerializationReportFields.RuntimeVersion] = ".NET 100.0.0";
                return true;
            case "wrong-os":
                reports[0]![NativeSerializationReportFields.HostEnvironmentInfo]![NativeSerializationReportFields.OsVersion] = "Windows 11";
                return true;
            case "mixed-host":
                reports[1]![NativeSerializationReportFields.HostEnvironmentInfo]![NativeSerializationReportFields.ProcessorName] = "Other CPU";
                return true;
            default:
                return false;
        }
    }

    internal static void Corpus(JsonObject corpus, string fault)
    {
        switch (fault)
        {
            case "valid":
                break;
            case "wrong-fixture":
                corpus[NativeSerializationReportFields.Fixture] = "OtherFixture";
                break;
            case "wrong-size":
                corpus[NativeSerializationReportFields.PayloadBytes] = 1025;
                break;
            case "wrong-hash":
                corpus[NativeSerializationReportFields.NativeSha256] = new string('z', 64);
                break;
            case "different-content":
                corpus[NativeSerializationReportFields.CorpusSha256] = new string('c', 64);
                break;
            case "empty-native":
                corpus[NativeSerializationReportFields.NativeBytes] = 0;
                break;
            case "undersized-native":
                corpus[NativeSerializationReportFields.NativeBytes] = 1023;
                break;
            case "extra-field":
                corpus[NativeSerializationReportFields.Unexpected] = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private static void ChangeDisplay(JsonNode benchmark, string oldValue, string newValue)
        => benchmark[NativeSerializationReportFields.DisplayInfo] = benchmark[NativeSerializationReportFields.DisplayInfo]!.GetValue<string>().Replace(oldValue, newValue, StringComparison.Ordinal);

    private static void FailedChild(JsonNode benchmark)
    {
        // Authentic failed BDN0.15.8 child shape from CI37120641864; no fabricated success measurements.
        benchmark[NativeSerializationReportFields.Statistics] = null;
        benchmark[NativeSerializationReportFields.Memory]![NativeSerializationReportFields.BytesAllocatedPerOperation] = null;
        benchmark[NativeSerializationReportFields.Memory]![NativeSerializationReportFields.TotalOperations] = 0;
        benchmark[NativeSerializationReportFields.Measurements] = new JsonArray();
    }

    private static void Measurement(JsonArray measurements, string fault)
    {
        var stage = fault switch
        {
            "missing-warmup" => "Warmup",
            "missing-actual" => "Actual",
            _ => "Result"
        };
        var row = measurements.First(value => value![NativeSerializationReportFields.IterationStage]!.GetValue<string>() == stage)!;
        switch (fault)
        {
            case "missing-warmup":
            case "missing-actual":
            case "missing-result":
                measurements.Remove(row);
                break;
            case "duplicate-iteration":
                measurements.Add(row.DeepClone());
                break;
            case "wrong-launch":
                row[NativeSerializationReportFields.LaunchIndex] = 3;
                break;
            case "zero-time":
                row[NativeSerializationReportFields.Nanoseconds] = 0d;
                break;
            case "zero-operations":
                row[NativeSerializationReportFields.Operations] = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
