using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsStoreTestData
{
    internal const string Store = "user-analytics";
    internal const string AnchorCollection = "analytics-anchors";
    internal const string AnchorId = "anchor";
    internal const string AnchorJson = "{\"name\":\"canonical-anchor\"}";
    internal const string Root = "root";
    internal const string Reader = "analytics-reader";
    internal const string Owner = "analytics-reader-owner";
    internal const string Other = "analytics-hidden-owner";
    internal const string Kind = "kind";
    internal const string Value = "value";
    internal const string MetricPath = "/metrics/value";
    internal const string ReadGrant = "analytics-value-read";
    internal const string UseGrant = "analytics-value-use";
    internal const string WriteGrant = "analytics-value-write";
    internal const string Classification = "private";
    internal const string First = "a";
    internal const string Second = "b";
    internal const string Third = "c";
    internal const string Hidden = "hidden";
    internal const string Alpha = "alpha";
    internal const string Beta = "beta";
    internal const string Count = "count";
    internal const string Sum = "sum";
    internal const string Average = "average";
    internal const string Minimum = "minimum";
    internal const string Maximum = "maximum";
    internal static readonly DateTimeOffset From = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset To = From.AddHours(1);

    internal static AnalyticsEventData Event(TestDatabase db, string id, string kind, double value, bool owned = false)
        => new(id, From.AddMinutes(1), ImmutableDictionary<string, string>.Empty.Add(Kind, kind),
            ImmutableDictionary<string, double>.Empty.Add(Value, value), owned ? new(OwnerId: Owner) : null,
            [new(db.Partition, AnchorCollection, AnchorId)]);

    internal static AggregateAnalyticsRequest Query(TestDatabase db) => new(db.Partition, Store, From, To,
        [Kind], [new(Count, string.Empty, AnalyticsAggregateKind.Count), new(Sum, Value, AnalyticsAggregateKind.Sum),
        new(Average, Value, AnalyticsAggregateKind.Average), new(Minimum, Value, AnalyticsAggregateKind.Min),
        new(Maximum, Value, AnalyticsAggregateKind.Max)], 10);

    internal static ReadAnalyticsEventsRequest Read(TestDatabase db) => new(db.Partition, Store, From, To, 10, null);
    internal static PrincipalRecord Principal(TestDatabase db, long epoch, ImmutableArray<string> grants) => new(Reader,
        db.Partition.TenantId, [new(db.Partition.DatabaseId, Store, Capability.AnalyticsRead),
        new(db.Partition.DatabaseId, AnchorCollection, Capability.DocumentsRead)], grants)
    {
        PolicyEpoch = epoch,
        RestrictRows = true,
        OwnerId = Owner
    };

    internal static AnalyticsAggregateRow Row(string kind, double count, double sum, double average, double min, double max)
        => new(ImmutableDictionary<string, string>.Empty.Add(Kind, kind),
            ImmutableDictionary<string, double>.Empty.Add(Count, count).Add(Sum, sum).Add(Average, average).Add(Minimum, min).Add(Maximum, max));
}
