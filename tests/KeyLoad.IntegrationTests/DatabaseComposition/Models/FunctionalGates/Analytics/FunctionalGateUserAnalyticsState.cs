using System.Collections.Immutable;
using System.Globalization;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateUserAnalyticsState(string Store, DateTimeOffset From, string Cohort,
    AnalyticsEventData First, AnalyticsEventData Second, AnalyticsEventData Third)
{
    private const string CohortPrefix = "analytics-";
    private const string Separator = "-";
    private const string ResourceSeparator = "_";
    private const string FirstSuffix = "-a";
    private const string SecondSuffix = "-b";
    private const string ThirdSuffix = "-c";
    private const int Year = 2026;
    private const int Month = 10;
    private const int Day = 1;
    private const int SecondOffset = 1;
    private const int ThirdOffset = 2;
    private const double FirstValue = 2;
    private const double SecondValue = 4;
    private const double ThirdValue = 8;
    internal const double UpdatedValue = 10;
    private static readonly DateTimeOffset Origin = new(Year, Month, Day, P.Empty, P.Empty, P.Empty, TimeSpan.Zero);

    internal static FunctionalGateUserAnalyticsState Create(FunctionalGateContext context, int index, int iteration)
    {
        var cohort = CohortPrefix + iteration.ToString(CultureInfo.InvariantCulture) + Separator
            + Guid.NewGuid().ToString(FunctionalGateProtocol.GuidFormat);
        var from = Origin.AddMinutes(iteration);
        AnalyticsEventData Event(string suffix, int offset, string kind, double value)
            => new(cohort + suffix, from.AddSeconds(offset),
                ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal)
                    .Add(P.Cohort, cohort).Add(P.Kind, kind),
                ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal).Add(P.Value, value).Add(P.Weight, P.WeightValue),
                null, [context.Bind(context.Anchor)]);
        return new(StoreName(context.WriterPrincipalId, index), from, cohort,
            Event(FirstSuffix, P.Empty, P.Alpha, FirstValue),
            Event(SecondSuffix, SecondOffset, P.Alpha, SecondValue),
            Event(ThirdSuffix, ThirdOffset, P.Beta, ThirdValue));
    }

    internal AnalyticsEventData Updated => First with
    {
        Metrics = ImmutableDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal).Add(P.Value, UpdatedValue).Add(P.Weight, P.WeightValue)
    };
    internal DateTimeOffset Until => From.AddMinutes(SecondOffset);
    internal static string StoreName(string writerPrincipalId, int index)
        => P.Prefix + writerPrincipalId + ResourceSeparator + index.ToString(CultureInfo.InvariantCulture);
    internal static AnalyticsEventRecord Record(AnalyticsEventData value, long revision)
        => new(value.Id, value.Timestamp, value.Dimensions, value.Metrics, value.RowAccess, value.Links, revision);
}
