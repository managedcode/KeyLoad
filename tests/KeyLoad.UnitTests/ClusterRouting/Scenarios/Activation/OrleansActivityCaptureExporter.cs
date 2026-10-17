using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry;

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
