using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class OrleansActivityCaptureExporter : BaseExporter<Activity>
{
    private readonly System.Threading.Lock gate = new();
    private readonly List<OrleansActivityCapture> records = [];

    internal OrleansActivityCapture[] Snapshot()
    {
        lock (gate)
        {
            return [.. records];
        }
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        lock (gate)
        {
            foreach (var activity in batch)
            {
                if (records.Count >= OrleansRuntimeTelemetryTokens.MaximumCapturedRecords)
                {
                    break;
                }

                records.Add(OrleansActivityCapture.From(activity));
            }
        }

        return ExportResult.Success;
    }
}

internal sealed class OrleansMetricCaptureExporter : BaseExporter<Metric>
{
    private readonly System.Threading.Lock gate = new();
    private readonly List<OrleansMetricPointCapture> records = [];

    internal OrleansMetricPointCapture[] Snapshot()
    {
        lock (gate)
        {
            return [.. records];
        }
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        lock (gate)
        {
            foreach (var metric in batch)
            {
                foreach (var point in metric.GetMetricPoints())
                {
                    if (records.Count >= OrleansRuntimeTelemetryTokens.MaximumCapturedRecords)
                    {
                        return ExportResult.Success;
                    }

                    var exemplarTags = new List<KeyValuePair<string, string?>>();
                    if (point.TryGetExemplars(out var exemplars))
                    {
                        foreach (var exemplar in exemplars)
                        {
                            exemplarTags.AddRange(exemplar.FilteredTags.Select(static tag =>
                                new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())));
                        }
                    }

                    records.Add(new(metric.MeterName, metric.Name,
                        point.Tags.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray(),
                        exemplarTags.ToArray()));
                }
            }
        }

        return ExportResult.Success;
    }
}

internal sealed record OrleansActivityCapture(string SourceName, string DisplayName, string TraceId,
    string SpanId, string ParentSpanId, ActivityStatusCode Status, string? StatusDescription,
    string? TraceState, string[] Baggage, KeyValuePair<string, string?>[] Tags,
    OrleansActivityEventCapture[] Events)
{
    internal static OrleansActivityCapture From(Activity activity)
        => new(activity.Source.Name, activity.DisplayName, activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(), activity.ParentSpanId.ToHexString(), activity.Status,
            activity.StatusDescription, activity.TraceStateString,
            activity.Baggage.Select(static item => item.Key + "=" + item.Value).ToArray(),
            activity.TagObjects.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray(),
            activity.Events.Select(static activityEvent => new OrleansActivityEventCapture(activityEvent.Name,
                activityEvent.Tags.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray())).ToArray());
}

internal sealed record OrleansActivityEventCapture(string Name, KeyValuePair<string, string?>[] Tags);

internal sealed record OrleansMetricPointCapture(string MeterName, string MetricName,
    KeyValuePair<string, string?>[] Tags, KeyValuePair<string, string?>[] ExemplarTags);
