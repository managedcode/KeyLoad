using System.Collections.Immutable;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsPolicyState(FunctionalGateUserAnalyticsState Query,
    AnalyticsEventData Owned, AnalyticsEventData Foreign)
{
    private const int Year = 2026;
    private const int Month = 12;
    private const int Day = 1;
    private const int Zero = 0;
    private const int One = 1;
    private const int BothEvents = 2;
    private const double OwnedValue = 2;
    private const double ForeignValue = 99;
    private const string ForeignOwner = "unrelated-analytics-owner";
    private const string Prefix = "analytics-policy-";
    private const string OwnedSuffix = "-a";
    private const string ForeignSuffix = "-b";

    internal static FunctionalGateAnalyticsPolicyState Create(string store, string principalId)
    {
        var id = Prefix + Guid.NewGuid().ToString(FunctionalGateProtocol.GuidFormat);
        var from = new DateTimeOffset(Year, Month, Day, Zero, Zero, Zero, TimeSpan.Zero);
        AnalyticsEventData Event(string suffix, string owner, string kind, double value)
            => new(id + suffix, from, ImmutableDictionary<string, string>.Empty.Add(P.Cohort, id).Add(P.Kind, kind),
                ImmutableDictionary<string, double>.Empty.Add(P.Value, value).Add(P.Weight, P.WeightValue), new(OwnerId: owner), []);
        var owned = Event(OwnedSuffix, principalId, P.Alpha, OwnedValue);
        var foreign = Event(ForeignSuffix, ForeignOwner, P.Beta, ForeignValue);
        return new(new(store, from, id, owned, foreign, foreign), owned, foreign);
    }

    internal static AnalyticsAggregateResult FullExpected => new(
        [Row(P.Alpha, OwnedValue), Row(P.Beta, ForeignValue)], BothEvents);

    private static AnalyticsAggregateRow Row(string kind, double value)
        => new(ImmutableDictionary<string, string>.Empty.Add(P.Kind, kind),
            ImmutableDictionary<string, double>.Empty.Add(P.Count, One).Add(P.Sum, value)
                .Add(P.Average, value).Add(P.Minimum, value).Add(P.Maximum, value));

    internal static AnalyticsAggregateResult Expected => new(
        [new(ImmutableDictionary<string, string>.Empty.Add(P.Kind, P.Alpha),
            ImmutableDictionary<string, double>.Empty.Add(P.Count, One).Add(P.Sum, OwnedValue)
                .Add(P.Average, OwnedValue).Add(P.Minimum, OwnedValue).Add(P.Maximum, OwnedValue))], One);
}
