using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.ClientApi;

internal static class McpAnalyticsDropDiscoveryProtocol
{
    internal const string Store = "discovered_analytics";
    internal const string Table = "discovered_table";
    internal const string Anchor = "discovered_anchor";
    internal const string Id = "id";
    internal const string AnchorId = "anchor";
    internal const string OldId = "old-row";
    internal const string FreshId = "fresh-row";
    internal const string First = "event-a";
    internal const string Second = "event-b";
    internal const string Kind = "kind";
    internal const string Alpha = "alpha";
    internal const string Value = "value";
    internal const string Count = "count";
    internal const string Sum = "sum";
    internal const string Average = "average";
    internal const string Minimum = "minimum";
    internal const string Maximum = "maximum";
    internal const string DropTool = "keyload_tables_drop";
    internal const string AnchorJson = "{\"anchor\":true}";
    internal const string OldJson = "{\"id\":\"old-row\",\"value\":11}";
    internal const string FreshJson = "{\"id\":\"fresh-row\",\"value\":37}";
    internal const string CreateSql = "CREATE TABLE [discovered_table] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,value BIGINT NOT NULL)";
    internal static readonly DateTimeOffset From = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    internal static DateTimeOffset Until => From.AddMinutes(1);
    internal static ReadAnalyticsEventsRequest Read(PartitionRef partition) => new(partition, Store, From, Until, 2, null);
    internal static AggregateAnalyticsRequest Query(PartitionRef partition) => new(partition, Store, From, Until,
        [Kind], [new(Count, string.Empty, AnalyticsAggregateKind.Count), new(Sum, Value, AnalyticsAggregateKind.Sum),
            new(Average, Value, AnalyticsAggregateKind.Average), new(Minimum, Value, AnalyticsAggregateKind.Min),
            new(Maximum, Value, AnalyticsAggregateKind.Max)], 1);
    internal static AnalyticsEventData Event(string id, double value, EntityRef anchor) => new(id, From,
        ImmutableDictionary<string, string>.Empty.Add(Kind, Alpha),
        ImmutableDictionary<string, double>.Empty.Add(Value, value), null, [anchor]);
    internal static AnalyticsEventsPage Expected(EntityRef anchor)
    {
        AnalyticsEventRecord Record(string id, double value) => new(id, From,
            ImmutableDictionary<string, string>.Empty.Add(Kind, Alpha),
            ImmutableDictionary<string, double>.Empty.Add(Value, value), null, [anchor], 1);
        return new([Record(First, 2), Record(Second, 6)], false, null);
    }
    internal static AnalyticsAggregateResult Aggregate => new([new(
        ImmutableDictionary<string, string>.Empty.Add(Kind, Alpha),
        ImmutableDictionary<string, double>.Empty.Add(Count, 2).Add(Sum, 8).Add(Average, 4)
            .Add(Minimum, 2).Add(Maximum, 6))], 2);
}
