namespace KeyLoad.Core.Features.TimeSeries;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(SampleRollupNative.Alias)]
internal sealed record SampleRollupState(
    [property: global::Orleans.Id(SampleRollupNative.Format)] int FormatVersion,
    [property: global::Orleans.Id(SampleRollupNative.From)] long FromUtcTicks,
    [property: global::Orleans.Id(SampleRollupNative.Until)] long UntilUtcTicks,
    [property: global::Orleans.Id(SampleRollupNative.Revision)] long Revision,
    [property: global::Orleans.Id(SampleRollupNative.Sequence)] long SourceSequence,
    [property: global::Orleans.Id(SampleRollupNative.Floor)] long? RetentionBeforeUtcTicks,
    [property: global::Orleans.Id(SampleRollupNative.Dropped)] bool Dropped,
    [property: global::Orleans.Id(SampleRollupNative.Count)] long Count,
    [property: global::Orleans.Id(SampleRollupNative.Sum)] double Sum,
    [property: global::Orleans.Id(SampleRollupNative.Minimum)] double? Minimum,
    [property: global::Orleans.Id(SampleRollupNative.Maximum)] double? Maximum);
