namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRetentionNativeAliases
{
    internal const string State = "keyload.core.v1.SampleRetentionState";
}

internal static class SampleRetentionStateFields
{
    internal const int FormatVersion = 0;
    internal const int BeforeUtcTicks = 1;
    internal const int PurgedCount = 2;
    internal const int HasMore = 3;
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(SampleRetentionNativeAliases.State)]
internal sealed record SampleRetentionState(
    [property: global::Orleans.Id(SampleRetentionStateFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(SampleRetentionStateFields.BeforeUtcTicks)] long BeforeUtcTicks,
    [property: global::Orleans.Id(SampleRetentionStateFields.PurgedCount)] long PurgedCount,
    [property: global::Orleans.Id(SampleRetentionStateFields.HasMore)] bool HasMore);
