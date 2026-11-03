using ZoneTree.Analytics;

namespace KeyLoad.Core.AnalyticsStore;

/// <summary>Prepares one query's field names and native keys before projecting its rows.</summary>
internal sealed class AnalyticsStoreEventMapping
{
    private readonly Field[] groups;
    private readonly Field[] metrics;
    private readonly AnalyticsStoreDimensions dimensions;

    internal AnalyticsStoreEventMapping(AnalyticsAggregateSnapshot snapshot, AnalyticsStoreDimensions dimensions)
    {
        this.dimensions = dimensions;
        var groupFields = new List<Field>(snapshot.Request.GroupBy.Length);
        foreach (var name in snapshot.Request.GroupBy)
        {
            snapshot.Work.Check();
            groupFields.Add(new(name, AnalyticsStoreMapping.Dimension(snapshot.Schema, name)));
        }
        groups = [.. groupFields];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var metricFields = new List<Field>();
        foreach (var selection in snapshot.Request.Metrics)
        {
            snapshot.Work.Check();
            if (selection.Operation != AnalyticsAggregateKind.Count && names.Add(selection.Metric))
            {
                metricFields.Add(new(selection.Metric, AnalyticsStoreMapping.Value(snapshot.Schema, selection.Metric)));
            }
        }
        metrics = [.. metricFields];
    }

    internal int MetricCount => metrics.Length;

    internal AnalyticsEvent Create(AnalyticsEventRecord row)
    {
        var value = new AnalyticsEvent(AnalyticsStoreOptions.Event)
        {
            Site = AnalyticsStoreOptions.Site,
            Timestamp = row.Timestamp,
            NonInteractive = true
        };
        foreach (var field in groups)
        {
            value.Dimensions.Add(field.Key, dimensions.ForEvent(field.Name, row.Dimensions[field.Name]));
        }
        foreach (var field in metrics)
        {
            value.Values.Add(field.Key, row.Metrics[field.Name]);
        }
        return value;
    }

    private readonly record struct Field(string Name, string Key);
}
