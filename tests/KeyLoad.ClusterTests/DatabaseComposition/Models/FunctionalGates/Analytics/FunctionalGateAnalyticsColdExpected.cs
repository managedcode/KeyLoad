using System.Collections.Immutable;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsColdExpected
{
    internal const int ContinuedRevision = 3;
    internal const long DeletedGeneration = 4;
    private const double ContinuedValue = 20;
    private const double AlphaCount = 2;
    private const double AlphaSum = 24;
    private const double AlphaAverage = 12;
    private const double AlphaMinimum = 4;
    private const double BetaCount = 1;
    private const double BetaValue = 8;
    private const long EventCount = 3;

    internal static AnalyticsEventData Continued(FunctionalGateUserAnalyticsState state)
        => state.First with
        {
            Metrics = ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal)
                .Add(P.Value, ContinuedValue).Add(P.Weight, P.WeightValue)
        };

    internal static ImmutableArray<AnalyticsEventRecord> UpdatedRows(FunctionalGateUserAnalyticsState state)
        => [FunctionalGateUserAnalyticsState.Record(state.Updated, P.UpdatedRevision),
            FunctionalGateUserAnalyticsState.Record(state.Second, P.FirstRevision),
            FunctionalGateUserAnalyticsState.Record(state.Third, P.FirstRevision)];

    internal static ImmutableArray<AnalyticsEventRecord> ContinuedRows(FunctionalGateUserAnalyticsState state)
        => [FunctionalGateUserAnalyticsState.Record(Continued(state), ContinuedRevision),
            FunctionalGateUserAnalyticsState.Record(state.Second, P.FirstRevision),
            FunctionalGateUserAnalyticsState.Record(state.Third, P.FirstRevision)];

    internal static AnalyticsAggregateResult ContinuedAggregate => new(
        [Row(P.Alpha, AlphaCount, AlphaSum, AlphaAverage, AlphaMinimum, ContinuedValue),
            Row(P.Beta, BetaCount, BetaValue, BetaValue, BetaValue, BetaValue)], EventCount);

    private static AnalyticsAggregateRow Row(string kind, double count, double sum,
        double average, double minimum, double maximum)
        => new(ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal).Add(P.Kind, kind),
            ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal)
                .Add(P.Count, count).Add(P.Sum, sum).Add(P.Average, average)
                .Add(P.Minimum, minimum).Add(P.Maximum, maximum));
}
