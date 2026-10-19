using System.Text.Json.Nodes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class SerializationReportMutations
{
    internal static void Apply(JsonArray reports, string fault)
    {
        var benchmarks = reports[0]![SerializationReportFields.Benchmarks]!.AsArray();
        var first = benchmarks[0]!;
        if (ApplyIdentityAndSettings(reports, benchmarks, first, fault)
            || ApplyStatisticsAndMemory(first, fault)
            || ApplyHost(reports, fault))
        {
            return;
        }
        Measurement(first[SerializationReportFields.Measurements]!.AsArray(), fault);
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
                first[SerializationReportFields.Type] = "OtherBenchmarks";
                return true;
            case "wrong-method":
                first[SerializationReportFields.Method] = "OtherMethod";
                return true;
            case "wrong-size":
                first[SerializationReportFields.Parameters] = "PayloadBytes=2048";
                return true;
            case "wrong-settings":
                first[SerializationReportFields.DisplayInfo] = first[SerializationReportFields.DisplayInfo]!.GetValue<string>().Replace("LaunchCount=2", "LaunchCount=1", StringComparison.Ordinal);
                return true;
            case "wrong-runtime-setting":
                ChangeDisplay(first, "Runtime=.NET 10.0", "Runtime=.NET 9.0");
                return true;
            case "in-process-setting":
                ChangeDisplay(first, "Runtime=.NET 10.0", "Runtime=.NET 10.0, Toolchain=InProcess");
                return true;
            case "duplicate-parameter":
                first[SerializationReportFields.Parameters] = "PayloadBytes=1024&PayloadBytes=1024";
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
                first[SerializationReportFields.Statistics] = null;
                return true;
            case "wrong-n":
                first[SerializationReportFields.Statistics]![SerializationReportFields.N] = 11;
                return true;
            case "wrong-original-values":
                first[SerializationReportFields.Statistics]![SerializationReportFields.OriginalValues]!.AsArray()[0] = 1d;
                return true;
            case "negative-deviation":
                first[SerializationReportFields.Statistics]![SerializationReportFields.StandardDeviation] = -1d;
                return true;
            case "negative-allocation":
                first[SerializationReportFields.Memory]![SerializationReportFields.BytesAllocatedPerOperation] = -1;
                return true;
            case "missing-memory":
                first.AsObject().Remove(SerializationReportFields.Memory);
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
                reports[0]![SerializationReportFields.HostEnvironmentInfo]![SerializationReportFields.BenchmarkDotNetVersion] = "0.15.7";
                return true;
            case "wrong-runtime":
                reports[0]![SerializationReportFields.HostEnvironmentInfo]![SerializationReportFields.RuntimeVersion] = ".NET 100.0.0";
                return true;
            case "wrong-os":
                reports[0]![SerializationReportFields.HostEnvironmentInfo]![SerializationReportFields.OsVersion] = "Windows 11";
                return true;
            case "mixed-host":
                reports[1]![SerializationReportFields.HostEnvironmentInfo]![SerializationReportFields.ProcessorName] = "Other CPU";
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
                corpus[SerializationReportFields.Fixture] = "OtherFixture";
                break;
            case "wrong-size":
                corpus[SerializationReportFields.PayloadBytes] = 1025;
                break;
            case "wrong-hash":
                corpus[SerializationReportFields.RuntimeSha256] = new string('z', 64);
                break;
            case "different-content":
                corpus[SerializationReportFields.CorpusSha256] = new string('c', 64);
                break;
            case "empty-execution":
                corpus[SerializationReportFields.RuntimeBytes] = 0;
                break;
            case "undersized-execution":
                corpus[SerializationReportFields.RuntimeBytes] = 1023;
                break;
            case "extra-field":
                corpus[SerializationReportFields.Unexpected] = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private static void ChangeDisplay(JsonNode benchmark, string oldValue, string newValue)
        => benchmark[SerializationReportFields.DisplayInfo] = benchmark[SerializationReportFields.DisplayInfo]!.GetValue<string>().Replace(oldValue, newValue, StringComparison.Ordinal);

    private static void FailedChild(JsonNode benchmark)
    {
        // Authentic failed BDN0.15.8 child shape from CI37120641864; no fabricated success measurements.
        benchmark[SerializationReportFields.Statistics] = null;
        benchmark[SerializationReportFields.Memory]![SerializationReportFields.BytesAllocatedPerOperation] = null;
        benchmark[SerializationReportFields.Memory]![SerializationReportFields.TotalOperations] = 0;
        benchmark[SerializationReportFields.Measurements] = new JsonArray();
    }

    private static void Measurement(JsonArray measurements, string fault)
    {
        var stage = fault switch
        {
            "missing-warmup" => "Warmup",
            "missing-actual" => "Actual",
            _ => "Result"
        };
        var row = measurements.First(value => value![SerializationReportFields.IterationStage]!.GetValue<string>() == stage)!;
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
                row[SerializationReportFields.LaunchIndex] = 3;
                break;
            case "zero-time":
                row[SerializationReportFields.Nanoseconds] = 0d;
                break;
            case "zero-operations":
                row[SerializationReportFields.Operations] = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }
}
