using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class OrleansMetricCaptureExporter : BaseExporter<Metric>
{
    private const long CounterStep = 1;
    private readonly System.Threading.Lock gate = new();
    private readonly List<OrleansMetricPointCapture> records = [];
    private IOptions<OrleansTelemetryCaptureOptions>? configuredOptions;
    private long exportCalls;
    private long matchingPoints;
    private long maximumPointsPerExport;
    private long truncatingExports;

    internal void Configure(IOptions<OrleansTelemetryCaptureOptions> options)
        => configuredOptions = options;

    internal bool WasTruncated
    {
        get
        {
            lock (gate)
            {
                return field;
            }
        }

        private set;
    }

    internal OrleansMetricPointCapture[] Snapshot()
    {
        lock (gate)
        {
            return [.. records];
        }
    }

    internal string DiagnosticSummary
    {
        get
        {
            lock (gate)
            {
                return FormattableString.Invariant(
                    $"export_calls={exportCalls}; matching_points_total={matchingPoints}; max_points_per_export={maximumPointsPerExport}; retained_rows={records.Count}; truncating_exports={truncatingExports}");
            }
        }
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        lock (gate)
        {
            exportCalls = AddSaturated(exportCalls, CounterStep);
            var pointsInExport = 0L;
            var truncated = false;
            foreach (var metric in batch)
            {
                CountAndCaptureMetric(metric, ref pointsInExport, ref truncated);
            }

            matchingPoints = AddSaturated(matchingPoints, pointsInExport);
            maximumPointsPerExport = Math.Max(maximumPointsPerExport, pointsInExport);
            if (truncated)
            {
                truncatingExports = AddSaturated(truncatingExports, CounterStep);
            }
        }

        return ExportResult.Success;
    }

    private int CaptureLimit => configuredOptions?.Value.MaximumRecords
                                ?? throw new InvalidOperationException("The execution metric exporter has no validated capture options.");

    private void CountAndCaptureMetric(Metric metric, ref long pointsInExport, ref bool truncated)
    {
        if (metric.MeterName != OrleansRuntimeTelemetryTokens.MetricMeter
            && metric.MeterName != OrleansRuntimeTelemetryTokens.PrivacyMeter)
        {
            return;
        }

        foreach (var point in metric.GetMetricPoints())
        {
            pointsInExport = AddSaturated(pointsInExport, CounterStep);
            var capture = new OrleansMetricPointCapture(metric.MeterName, metric.Name,
                CaptureTags(point.Tags), CaptureExemplarTags(point));
            if (records.Any(record => IsSameObservation(record, capture)))
            {
                continue;
            }

            if (records.Count >= CaptureLimit)
            {
                WasTruncated = true;
                truncated = true;
                continue;
            }

            records.Add(capture);
        }
    }

    private static bool IsSameObservation(OrleansMetricPointCapture left, OrleansMetricPointCapture right)
        => string.Equals(left.MeterName, right.MeterName, StringComparison.Ordinal)
           && string.Equals(left.MetricName, right.MetricName, StringComparison.Ordinal)
           && AreTagsEqual(left.Tags, right.Tags)
           && AreTagsEqual(left.ExemplarTags, right.ExemplarTags);

    private static bool AreTagsEqual(KeyValuePair<string, string?>[] left,
        KeyValuePair<string, string?>[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!string.Equals(left[index].Key, right[index].Key, StringComparison.Ordinal)
                || !string.Equals(left[index].Value, right[index].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static long AddSaturated(long current, long increment)
        => current > long.MaxValue - increment ? long.MaxValue : current + increment;

    private static KeyValuePair<string, string?>[] CaptureTags(ReadOnlyTagCollection tags)
    {
        var result = new List<KeyValuePair<string, string?>>(tags.Count);
        foreach (var tag in tags)
        {
            result.Add(new(tag.Key, tag.Value?.ToString()));
        }

        return result.ToArray();
    }

    private static KeyValuePair<string, string?>[] CaptureExemplarTags(MetricPoint point)
    {
        var tags = new List<KeyValuePair<string, string?>>();
        if (!point.TryGetExemplars(out var exemplars))
        {
            return [];
        }

        foreach (var exemplar in exemplars)
        {
            foreach (var tag in exemplar.FilteredTags)
            {
                tags.Add(new(tag.Key, tag.Value?.ToString()));
            }
        }

        return tags.ToArray();
    }
}
