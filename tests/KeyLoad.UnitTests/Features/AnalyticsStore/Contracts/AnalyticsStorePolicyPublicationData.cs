using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsStorePolicyPublicationData
{
    internal const long OriginalEpoch = 1;
    internal const long RevokedEpoch = 2;
    internal const long RestoredEpoch = 3;
    internal const long Revision = 1;
    internal const long Absent = 0;
    internal const double FirstValue = 2;
    internal const double SecondValue = 4;
    internal const double ThirdValue = 8;
    internal const string OtherCollection = "analytics-unrelated-publication";
    internal const string OtherId = "unrelated";
    internal const string OtherJson = "{\"purpose\":\"unrelated-source-position\"}";
    internal const string FieldDenied = "The query requires a protected field-use grant.";
    private const long InputEvents = 3;
    private const double AlphaCount = 2;
    private const double AlphaSum = 6;
    private const double AlphaAverage = 3;
    private const double BetaCount = 1;

    internal static AnalyticsAggregateResult Expected => new(
        [D.Row(D.Alpha, AlphaCount, AlphaSum, AlphaAverage, FirstValue, SecondValue),
            D.Row(D.Beta, BetaCount, ThirdValue, ThirdValue, ThirdValue, ThirdValue)], InputEvents);
}
