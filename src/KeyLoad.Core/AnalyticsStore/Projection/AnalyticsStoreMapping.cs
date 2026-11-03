using ZoneTree.Analytics;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreMapping
{
    private const string DimensionPrefix = "d";
    private const string MetricPrefix = "v";
    internal static string Dimension(AnalyticsStoreSchema schema, string name)
        => DimensionPrefix + schema.Dimensions.IndexOf(name).ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal static string Value(AnalyticsStoreSchema schema, string name)
        => MetricPrefix + schema.Metrics.IndexOf(name).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static Metric Metric(AnalyticsAggregateSnapshot snapshot, AnalyticsMetricSelection selection)
    {
        var key = Value(snapshot.Schema, selection.Metric);
        return selection.Operation switch
        {
            AnalyticsAggregateKind.Count => ZoneTree.Analytics.Metric.Events,
            AnalyticsAggregateKind.Sum => ZoneTree.Analytics.Metric.Sum(key),
            AnalyticsAggregateKind.Average => ZoneTree.Analytics.Metric.Average(key),
            AnalyticsAggregateKind.Min => ZoneTree.Analytics.Metric.Min(key),
            AnalyticsAggregateKind.Max => ZoneTree.Analytics.Metric.Max(key),
            _ => throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid)
        };
    }

    internal static AnalyticsQuery Query(AnalyticsAggregateSnapshot snapshot)
    {
        var request = snapshot.Request;
        var query = new AnalyticsQuery
        {
            Site = AnalyticsStoreOptions.Site,
            Event = AnalyticsStoreOptions.Event,
            Range = DateRange.Between(request.FromInclusive, request.ToExclusive),
            TimeZone = TimeZoneInfo.Utc,
            Fill = FillMode.None,
            Source = QuerySource.RawEvents,
            Limit = request.MaxGroups
        };
        foreach (var name in request.GroupBy)
        {
            query.GroupBy.Add(Dimension(snapshot.Schema, name));
        }

        query.Metrics.Add(ZoneTree.Analytics.Metric.Events);
        foreach (var selection in request.Metrics.Where(value => value.Operation != AnalyticsAggregateKind.Count)
            .DistinctBy(value => (value.Operation, value.Metric)))
        {
            query.Metrics.Add(Metric(snapshot, selection));
        }

        return query;
    }
}
