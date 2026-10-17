namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRollupNative
{
    internal const string Alias = "keyload.core.v1.SampleRollupState";
    internal const int Version = 1;
    internal const int Format = 0;
    internal const int From = 1;
    internal const int Until = 2;
    internal const int Revision = 3;
    internal const int Sequence = 4;
    internal const int Floor = 5;
    internal const int Dropped = 6;
    internal const int Count = 7;
    internal const int Sum = 8;
    internal const int Minimum = 9;
    internal const int Maximum = 10;
}

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
