using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsSharedProtocol
{
    internal const string Store = "analytics-shared";
    internal const string Documents = "analytics-links";
    internal const string Root = "root";
    internal const string EventId = "event-one";
    internal const string Anchor = "anchor";
    internal const string Effect = "effect";
    internal const string Dimension = "region";
    internal const string Metric = "amount";
    internal const string OtherMetric = "other-amount";
    internal const string Region = "north";
    internal const string Json = "{\"value\":1}";
    internal const string PutKind = "putDocument";
    internal const string Projection = "analytics-shared-projection";
    internal const string AnalyticsPartition = "analytics-only";
    internal static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch.AddDays(1);
    internal static readonly ImmutableDictionary<string, string> Dimensions = ImmutableDictionary<string, string>.Empty.Add(Dimension, Region);
    internal static readonly ImmutableDictionary<string, double> Metrics = ImmutableDictionary<string, double>.Empty.Add(Metric, 10d);
    internal static AnalyticsEventData Event(ImmutableArray<EntityRef> links)
        => new(EventId, Epoch, Dimensions, Metrics, null, links);
    internal static UpsertAnalyticsEvents Upsert(ImmutableArray<EntityRef> links, long expected = 0)
        => new(Store, [Event(links)], ImmutableDictionary<string, long>.Empty.Add(EventId, expected));
}
