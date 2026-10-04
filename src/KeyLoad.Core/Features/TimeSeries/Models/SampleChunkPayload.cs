namespace KeyLoad.Core.Features.TimeSeries;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(SampleChunkWire.PayloadAlias)]
internal sealed record SampleChunkPayload(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] int RecordCount,
    [property: global::Orleans.Id(2)] ReadOnlyMemory<byte> UtcTicks,
    [property: global::Orleans.Id(3)] ReadOnlyMemory<byte> Offsets,
    [property: global::Orleans.Id(4)] ReadOnlyMemory<byte> Sequences,
    [property: global::Orleans.Id(5)] ReadOnlyMemory<byte> Values,
    [property: global::Orleans.Id(6)] ReadOnlyMemory<byte> Series,
    [property: global::Orleans.Id(7)] ReadOnlyMemory<byte> EventIds,
    [property: global::Orleans.Id(8)] ReadOnlyMemory<byte> Tags,
    [property: global::Orleans.Id(9)] ReadOnlyMemory<byte> Checksum);
