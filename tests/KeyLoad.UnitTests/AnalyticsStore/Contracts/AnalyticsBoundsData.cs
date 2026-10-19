using System.Collections.Immutable;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundsData
{
    internal const string HealthyStore = "analytics-bounds-healthy";
    internal const string RowPrefix = "bound-row-";
    internal const string Padded = " x ";
    internal const string Plain = "x";
    internal const string Composed = "é";
    internal const string Decomposed = "e\u0301";
    internal const string Control = "\t";
    internal const string Changed = "changed";
    internal const int SourceRows = 65;
    internal const int ScanLimit = SourceRows - 1;
    internal const string UpsertKind = "upsertAnalyticsEvents";
    internal const string DeleteKind = "deleteAnalyticsEvents";

    internal static string Id(int index) => RowPrefix + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
    internal static AnalyticsAggregateRow Row(string group, double count, double sum)
        => new(ImmutableDictionary<string, string>.Empty.Add(D.Kind, group),
            ImmutableDictionary<string, double>.Empty.Add(D.Count, count).Add(D.Sum, sum));
    internal static AggregateAnalyticsRequest Query(TestDatabase db, string? store = null)
        => D.Query(db) with
        {
            Store = store ?? D.Store,
            Metrics = [new(D.Count, string.Empty, AnalyticsAggregateKind.Count), new(D.Sum, D.Value, AnalyticsAggregateKind.Sum)]
        };
}
