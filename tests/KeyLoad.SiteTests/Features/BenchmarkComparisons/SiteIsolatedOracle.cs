using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteIsolatedSelection(string Scenario, int NodeCount, object Repetition, string Metric, string Target);
internal sealed record SiteIsolatedExpectedRow(string Name, string Status, double? Value, int NodeCount,
    string WorkerId, long JobId, long ArtifactId);

internal static class SiteIsolatedOracle
{
    public static SiteIsolatedExpectedRow[] Rows(JsonElement projection, SiteIsolatedSelection selection)
    {
        var rows = new List<SiteIsolatedExpectedRow>();
        foreach (var worker in projection.GetProperty(SiteIsolatedFields.Workers).EnumerateArray())
        {
            var target = worker.GetProperty(SiteIsolatedFields.Target).GetString()!;
            if (worker.GetProperty(SiteIsolatedFields.Scenario).GetString() != selection.Scenario ||
                worker.GetProperty(SiteIsolatedFields.NodeCount).GetInt32() != selection.NodeCount ||
                selection.Target != "all" && selection.Target != target)
            {
                continue;
            }
            var report = worker.GetProperty(SiteIsolatedFields.Report);
            var cases = report.ValueKind == JsonValueKind.Null ? [] : report.GetProperty(SiteIsolatedFields.Cases).EnumerateArray()
                .Where(item => selection.Repetition is string || item.GetProperty(SiteIsolatedFields.Repetition).GetInt32() == (int)selection.Repetition).ToArray();
            var measured = cases.Where(item => item.GetProperty(SiteIsolatedFields.Measurement).ValueKind != JsonValueKind.Null)
                .Select(item => item.GetProperty(SiteIsolatedFields.Measurement)).ToArray();
            var status = report.ValueKind == JsonValueKind.Null ? "unsupportedTopology" : measured.Length == 0 ? "unsupported" : "measured";
            var value = Median(measured.Select(item => Metric(item, selection.Metric))
                .Where(item => item is not null).Select(item => item!.Value).ToArray());
            rows.Add(new(target, status, value, selection.NodeCount, worker.GetProperty(SiteIsolatedFields.Id).GetString()!,
                worker.GetProperty(SiteIsolatedFields.Job).GetProperty(SiteIsolatedFields.Id).GetInt64(), worker.GetProperty(SiteIsolatedFields.Artifact).GetProperty(SiteIsolatedFields.Id).GetInt64()));
        }
        return selection.Metric == "throughput"
            ? rows.OrderBy(row => row.Value is null).ThenByDescending(row => row.Value).ToArray()
            : rows.OrderBy(row => row.Value is null).ThenBy(row => row.Value).ToArray();
    }

    private static double? Metric(JsonElement measurement, string metric) => metric switch
    {
        "throughput" => measurement.GetProperty(SiteIsolatedFields.UsefulOperationsPerSecond).GetDouble(),
        "p50" => measurement.GetProperty(SiteIsolatedFields.Latency).GetProperty(SiteIsolatedFields.P50Ms).GetDouble(),
        "p95" => measurement.GetProperty(SiteIsolatedFields.Latency).GetProperty(SiteIsolatedFields.P95Ms).GetDouble(),
        "p99" => measurement.GetProperty(SiteIsolatedFields.Latency).GetProperty(SiteIsolatedFields.P99Ms).GetDouble(),
        "errors" => measurement.GetProperty(SiteIsolatedFields.Failures).GetDouble() / measurement.GetProperty(SiteIsolatedFields.Attempts).GetDouble() * 100,
        "enqueue" or "receive" or "ack" => measurement.GetProperty(metric).ValueKind == JsonValueKind.Null ? null :
            measurement.GetProperty(metric).GetProperty(SiteIsolatedFields.P99Ms).GetDouble(),
        "cpu" => measurement.GetProperty(SiteIsolatedFields.ClientResources).GetProperty(SiteIsolatedFields.CpuSeconds).GetDouble() * 1000 /
            measurement.GetProperty(SiteIsolatedFields.Attempts).GetDouble(),
        "alloc" => measurement.GetProperty(SiteIsolatedFields.ClientResources).GetProperty(SiteIsolatedFields.AllocatedBytes).GetDouble() / 1024 /
            measurement.GetProperty(SiteIsolatedFields.Attempts).GetDouble(),
        "rss" => measurement.GetProperty(SiteIsolatedFields.ClientResources).GetProperty(SiteIsolatedFields.PeakObservedWorkingSetBytes).GetDouble() / 1048576,
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };

    private static double? Median(double[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
    }
}
