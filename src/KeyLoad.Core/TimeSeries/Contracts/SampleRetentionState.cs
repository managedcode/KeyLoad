namespace KeyLoad.Core.Features.TimeSeries;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(SampleRetentionNativeAliases.State)]
internal sealed record SampleRetentionState(
    [property: global::Orleans.Id(SampleRetentionStateFields.FormatVersion)] int FormatVersion,
    [property: global::Orleans.Id(SampleRetentionStateFields.BeforeUtcTicks)] long BeforeUtcTicks,
    [property: global::Orleans.Id(SampleRetentionStateFields.PurgedCount)] long PurgedCount,
    [property: global::Orleans.Id(SampleRetentionStateFields.HasMore)] bool HasMore);
