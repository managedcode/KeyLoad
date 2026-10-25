using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Independent execution arithmetic for a single original case or concurrent lane, without repetition pooling.</summary>
internal static class SiteIsolatedOracle
{
    private const string Images = "images";
    private const string Server = "server";
    internal static readonly string[] Cases = ["load-million", "index", "sequential-read", "random-read", "search", "mixed", "verify-final",
        "mixed/create", "mixed/delete", "mixed/update", "mixed/read", "mixed/search"];

    public static SiteIsolatedExpectedRow[] Rows(JsonElement projection, SiteIsolatedSelection selection)
    {
        var rows = projection.GetProperty(SiteIsolatedFields.Workers).EnumerateArray()
            .Where(worker => worker.GetProperty(SiteIsolatedFields.NodeCount).GetInt32() == selection.NodeCount
                && (selection.Target == "all" || selection.Target == worker.GetProperty(SiteIsolatedFields.Target).GetString()))
            .Select(worker => Row(worker, selection));
        return selection.Metric == "throughput" ? rows.OrderBy(row => row.Value is null).ThenByDescending(row => row.Value).ToArray()
            : rows.OrderBy(row => row.Value is null).ThenBy(row => row.Value).ToArray();
    }

    private static SiteIsolatedExpectedRow Row(JsonElement worker, SiteIsolatedSelection selection)
    {
        var parts = selection.Scenario.Split('/');
        var report = worker.GetProperty(SiteIsolatedFields.Reports).EnumerateArray()
            .Single(item => item.GetProperty(SiteIsolatedFields.CaseId).GetString() == parts[0]);
        var status = report.GetProperty(SiteIsolatedFields.Status).GetString()!;
        var profile = worker.GetProperty(SiteIsolatedFields.Session).GetProperty(SiteIsolatedFields.TargetProfile);
        var qualified = Qualified(worker, profile);
        var lane = (SelectedLane(report, parts)).Value;
        var value = (Value(report, lane, status, qualified, selection.Metric)).Value;
        var displayedStatus = status == "notQualified" || status == "measured" && !qualified ? "failed"
            : status == "unavailable" ? TopologyUnavailable(worker) ? "unsupportedTopology" : "unsupported" : status;
        return new(worker.GetProperty(SiteIsolatedFields.Target).GetString()!, displayedStatus, value,
            selection.NodeCount, worker.GetProperty(SiteIsolatedFields.Id).GetString()!,
            worker.GetProperty(SiteIsolatedFields.Job).GetProperty(SiteIsolatedFields.Id).GetInt64(),
            worker.GetProperty(SiteIsolatedFields.Artifact).GetProperty(SiteIsolatedFields.Id).GetInt64());
    }

    private static bool TopologyUnavailable(JsonElement worker)
        => worker.GetProperty(SiteIsolatedFields.NodeCount).GetInt32() == 3
            && new[] { "Neo4j", "SurrealDB", "HelixDB" }.Contains(worker.GetProperty(SiteIsolatedFields.Target).GetString(), StringComparer.Ordinal)
            && worker.GetProperty(SiteIsolatedFields.Session).GetProperty(Images).GetProperty(Server).ValueKind == JsonValueKind.Null;

    private static bool Qualified(JsonElement worker, JsonElement profile)
        => worker.GetProperty(SiteIsolatedFields.ResourceQualified).GetBoolean()
            && worker.GetProperty(SiteIsolatedFields.Qualification).GetProperty(SiteIsolatedFields.Status).GetString() != "failed"
            && profile.ValueKind != JsonValueKind.Null
            && !new[] { profile.GetProperty(SiteIsolatedFields.Version).GetString()!,
                profile.GetProperty(SiteIsolatedFields.Topology).GetString()!,
                profile.GetProperty(SiteIsolatedFields.Cluster).GetProperty(SiteIsolatedFields.State).GetString()! }
                .Any(value => Regex.IsMatch(value, "unverified|unobserved|unknown|not recorded", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static (bool HasValue, JsonElement? Value) SelectedLane(JsonElement report, string[] parts)
    {
        var lanes = report.GetProperty(SiteIsolatedFields.Lanes).EnumerateArray().ToArray();
        return parts.Length == 2 ? ((JsonElement?)(lanes.Cast<JsonElement?>()
            .SingleOrDefault(item => item!.Value.GetProperty(SiteIsolatedFields.Id).GetString() == parts[1]))) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default(JsonElement?))
            : lanes.Length == 1 ? (true, ((JsonElement?)(lanes[0]))) : (false, default(JsonElement?));
    }

    private static (bool HasValue, double? Value) Value(JsonElement report, JsonElement? lane, string status, bool qualified, string metric)
    {
        if (status != "measured" || !qualified)
        {
            return (false, default(double?));
        }

        return metric == "duration"
            ? (true, ((double?)(lane?.GetProperty(SiteIsolatedFields.Measurement).GetProperty(SiteIsolatedFields.ElapsedSeconds).GetDouble()
                ?? report.GetProperty(SiteIsolatedFields.ElapsedSeconds).GetDouble())))
            : lane is null ? (false, default(double?)) : ((double?)((Metric(lane.Value.GetProperty(SiteIsolatedFields.Measurement), metric)).Value)) is { } outcomeValue4 ? (true, outcomeValue4) : (false, default(double?));
    }

    private static (bool HasValue, double? Value) Metric(JsonElement measurement, string metric)
    {
        var resources = measurement.GetProperty(SiteIsolatedFields.ClientResources);
        var attempts = measurement.GetProperty(SiteIsolatedFields.Attempts).GetDouble();
        return metric switch
        {
            "throughput" => (true, ((double?)(measurement.GetProperty(SiteIsolatedFields.UsefulOperationsPerSecond).GetDouble()))),
            "p50" or "p95" or "p99" => (true, ((double?)(measurement.GetProperty(SiteIsolatedFields.Latency).GetProperty(metric + SiteIsolatedFields.Ms).GetDouble()))),
            "errors" => attempts == 0 ? (true, (double?)(0)) : (true, ((double?)(measurement.GetProperty(SiteIsolatedFields.Failures).GetDouble() / attempts * 100))),
            "enqueue" or "receive" or "ack" => measurement.GetProperty(metric).ValueKind == JsonValueKind.Null ? (false, default(double?)) : (true, ((double?)(measurement.GetProperty(metric).GetProperty(SiteIsolatedFields.P99Ms).GetDouble()))),
            "cpu" => resources.ValueKind == JsonValueKind.Null || attempts == 0 ? (false, default(double?)) : (true, ((double?)(resources.GetProperty(SiteIsolatedFields.CpuSeconds).GetDouble() * 1000 / attempts))),
            "alloc" => resources.ValueKind == JsonValueKind.Null || attempts == 0 ? (false, default(double?)) : (true, ((double?)(resources.GetProperty(SiteIsolatedFields.AllocatedBytes).GetDouble() / 1024 / attempts))),
            "rss" => resources.ValueKind == JsonValueKind.Null ? (false, default(double?)) : (true, ((double?)(resources.GetProperty(SiteIsolatedFields.PeakObservedWorkingSetBytes).GetDouble() / 1048576))),
            _ => throw new ArgumentOutOfRangeException(nameof(metric))
        };
    }
}
