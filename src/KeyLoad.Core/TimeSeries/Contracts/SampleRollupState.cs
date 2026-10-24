namespace KeyLoad.Core.TimeSeries;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RuntimeSampleRollup.Alias)]
internal sealed record SampleRollupState(
    [property: global::Orleans.Id(RuntimeSampleRollup.Format)] int FormatVersion,
    [property: global::Orleans.Id(RuntimeSampleRollup.From)] long FromUtcTicks,
    [property: global::Orleans.Id(RuntimeSampleRollup.Until)] long UntilUtcTicks,
    [property: global::Orleans.Id(RuntimeSampleRollup.Revision)] long Revision,
    [property: global::Orleans.Id(RuntimeSampleRollup.Sequence)] long SourceSequence,
    [property: global::Orleans.Id(RuntimeSampleRollup.Floor)] long? RetentionBeforeUtcTicks,
    [property: global::Orleans.Id(RuntimeSampleRollup.Dropped)] bool Dropped,
    [property: global::Orleans.Id(RuntimeSampleRollup.Count)] long Count,
    [property: global::Orleans.Id(RuntimeSampleRollup.Sum)] double Sum,
    [property: global::Orleans.Id(RuntimeSampleRollup.Minimum)] double? Minimum,
    [property: global::Orleans.Id(RuntimeSampleRollup.Maximum)] double? Maximum);
