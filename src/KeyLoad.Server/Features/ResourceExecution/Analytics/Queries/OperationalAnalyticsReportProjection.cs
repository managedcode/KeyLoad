using System.Collections.Immutable;
using System.Text.Json;
using ZoneTree.Analytics;

namespace KeyLoad.Server;

internal static class OperationalAnalyticsReportProjection
{
    private const double Empty = 0;
    internal static OperationalAnalyticsReport Create(AnalyticsResult result, AnalyticsStatistics statistics,
        OperationalAnalyticsOptions settings)
    {
        if (result.Plan.UsedRawEvents
            || result.Plan.Steps.Any(step => step.Event != OperationalAnalyticsProtocol.Sample || step.Source != PlanSource.HourRollups)
            || result.GroupBy.Count != OperationalAnalyticsProtocol.DimensionCount
            || result.Metrics.Count != OperationalAnalyticsProtocol.MetricCount || result.Rows.Count > settings.MaximumRows
            || result.Rows.Count > settings.MaximumBuckets || result.TotalRows != result.Rows.Count)
        {
            throw new InvalidOperationException(OperationalAnalyticsProtocol.InvalidReport);
        }
        var rows = ImmutableArray.CreateBuilder<OperationalAnalyticsRow>(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            if (row.Bucket is not { } hour || row.Dimensions.Count != OperationalAnalyticsProtocol.DimensionCount
                || row.Values.Count != OperationalAnalyticsProtocol.MetricCount || row.Values.Any(value => !Valid(value)))
            {
                throw new InvalidOperationException(OperationalAnalyticsProtocol.InvalidReport);
            }
            rows.Add(new(hour, row[Metric.Events], row[Metric.Sum(OperationalAnalyticsProtocol.Committed)],
                row[Metric.Sum(OperationalAnalyticsProtocol.Applied)], row[Metric.Sum(OperationalAnalyticsProtocol.Backlog)]));
        }
        var report = new OperationalAnalyticsReport(result.From, result.To, result.Plan.IsComplete, rows.MoveToImmutable(),
            new(statistics.EventsAccepted, statistics.EventsProcessed, statistics.EventsDropped,
                statistics.EventsRejected, statistics.Errors, statistics.QueueLength));
        if (JsonSerializer.SerializeToUtf8Bytes(report, JsonDefaults.Options).Length > settings.MaximumReportBytes)
        {
            throw new InvalidOperationException(OperationalAnalyticsProtocol.InvalidReport);
        }
        return report;
    }

    private static bool Valid(double value)
        => double.IsFinite(value) && value >= Empty && value <= OperationalAnalyticsProtocol.MaximumExactInteger
            && Math.Truncate(value) == value;
}
