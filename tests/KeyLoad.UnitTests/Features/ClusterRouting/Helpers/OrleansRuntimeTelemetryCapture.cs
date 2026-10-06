using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class OrleansActivityCaptureExporter : BaseExporter<Activity>
{
    private readonly System.Threading.Lock gate = new();
    private readonly List<OrleansActivityCapture> records = [];
    private IOptions<OrleansTelemetryCaptureOptions>? configuredOptions;

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
                if (records.Count >= CaptureLimit)
                {
                    WasTruncated = true;
                    break;
                }

                records.Add(OrleansActivityCaptureMapper.From(activity));
            }
        }

        return ExportResult.Success;
    }

    private int CaptureLimit => configuredOptions?.Value.MaximumRecords
        ?? throw new InvalidOperationException("The native activity exporter has no validated capture options.");
}

internal sealed class OrleansMetricCaptureExporter : BaseExporter<Metric>
{
    private readonly System.Threading.Lock gate = new();
    private readonly List<OrleansMetricPointCapture> records = [];
    private IOptions<OrleansTelemetryCaptureOptions>? configuredOptions;

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

    public override ExportResult Export(in Batch<Metric> batch)
    {
        lock (gate)
        {
            foreach (var metric in batch)
            {
                if (!CaptureMetric(metric))
                {
                    return ExportResult.Success;
                }
            }
        }

        return ExportResult.Success;
    }

    private int CaptureLimit => configuredOptions?.Value.MaximumRecords
        ?? throw new InvalidOperationException("The native metric exporter has no validated capture options.");

    private bool CaptureMetric(Metric metric)
    {
        foreach (var point in metric.GetMetricPoints())
        {
            if (!CapturePoint(metric, point))
            {
                return false;
            }
        }

        return true;
    }

    private bool CapturePoint(Metric metric, MetricPoint point)
    {
        if (records.Count >= CaptureLimit)
        {
            WasTruncated = true;
            return false;
        }

        records.Add(new OrleansMetricPointCapture(metric.MeterName, metric.Name,
            CaptureTags(point.Tags), CaptureExemplarTags(point)));
        return true;
    }

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
            tags.AddRange(exemplar.FilteredTags.Select(static tag =>
                new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())));
        }

        return tags.ToArray();
    }
}

internal static class OrleansActivityCaptureMapper
{
    internal static OrleansActivityCapture From(Activity activity)
        => new(activity.Source.Name, activity.DisplayName, activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(), activity.ParentSpanId.ToHexString(), activity.Status,
            activity.StatusDescription, activity.TraceStateString,
            activity.Baggage.Select(static item => item.Key + "=" + item.Value).ToArray(),
            activity.Links.Any(),
            activity.TagObjects.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray(),
            activity.Events.Select(static activityEvent => new OrleansActivityEventCapture(activityEvent.Name,
                activityEvent.Tags.Select(static tag => new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString())).ToArray())).ToArray());
}
