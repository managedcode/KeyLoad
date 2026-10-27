using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class OrleansActivityCaptureExporter : BaseExporter<Activity>
{
    private readonly System.Threading.Lock gate = new();
    private readonly List<OrleansActivityCapture> records = [];
    private readonly HashSet<ActivityTraceId> ownedTraces = [];
    private IOptions<OrleansTelemetryCaptureOptions>? configuredOptions;

    internal void Configure(IOptions<OrleansTelemetryCaptureOptions> options)
        => configuredOptions = options;

    internal void IncludeTrace(ActivityTraceId traceId)
    {
        lock (gate)
        {
            if (ownedTraces.Count >= CaptureLimit)
            {
                throw new InvalidOperationException("The telemetry capture trace bound is exhausted.");
            }
            ownedTraces.Add(traceId);
        }
    }

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
                if (!ownedTraces.Contains(activity.TraceId))
                {
                    continue;
                }
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
        ?? throw new InvalidOperationException("The execution activity exporter has no validated capture options.");
}
