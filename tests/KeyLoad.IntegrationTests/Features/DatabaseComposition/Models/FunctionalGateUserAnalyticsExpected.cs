using System.Collections.Immutable;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Independent arithmetic literals, never values read from the native analytics engine.</summary>
internal static class FunctionalGateUserAnalyticsExpected
{
    private const int InitialEvents = 3;
    private const int RemainingEvents = 2;
    private const double AlphaCount = 2;
    private const double SingleCount = 1;
    private const double InitialSum = 6;
    private const double InitialAverage = 3;
    private const double InitialMinimum = 2;
    private const double InitialMaximum = 4;
    private const double BetaValue = 8;
    private const double UpdatedSum = 14;
    private const double UpdatedAverage = 7;
    private const double UpdatedMinimum = 4;
    private const double UpdatedMaximum = 10;

    internal static AnalyticsAggregateResult Initial => new(
        [Row(P.Alpha, count: AlphaCount, sum: InitialSum, average: InitialAverage, minimum: InitialMinimum, maximum: InitialMaximum),
            Row(P.Beta, count: SingleCount, sum: BetaValue, average: BetaValue, minimum: BetaValue, maximum: BetaValue)], EventCount: InitialEvents);
    internal static AnalyticsAggregateResult Updated => new(
        [Row(P.Alpha, count: AlphaCount, sum: UpdatedSum, average: UpdatedAverage, minimum: UpdatedMinimum, maximum: UpdatedMaximum),
            Row(P.Beta, count: SingleCount, sum: BetaValue, average: BetaValue, minimum: BetaValue, maximum: BetaValue)], EventCount: InitialEvents);
    internal static AnalyticsAggregateResult DeletedSecond => new(
        [Row(P.Alpha, count: SingleCount, sum: UpdatedMaximum, average: UpdatedMaximum, minimum: UpdatedMaximum, maximum: UpdatedMaximum),
            Row(P.Beta, count: SingleCount, sum: BetaValue, average: BetaValue, minimum: BetaValue, maximum: BetaValue)], EventCount: RemainingEvents);

    private static AnalyticsAggregateRow Row(string kind, double count, double sum, double average, double minimum, double maximum)
        => new(ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal).Add(P.Kind, kind),
            ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal)
                .Add(P.Count, count).Add(P.Sum, sum).Add(P.Average, average).Add(P.Minimum, minimum).Add(P.Maximum, maximum));
}
