using System.Collections.Immutable;

namespace KeyLoad.CrashHost;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeChecksumProfileProtocol.CutAlias)]
internal sealed record NativeChecksumProfileCut(
    [property: global::Orleans.Id(0)] Guid NodeId,
    [property: global::Orleans.Id(1)] Guid Incarnation,
    [property: global::Orleans.Id(2)] long Position,
    [property: global::Orleans.Id(3)] long ReadGeneration,
    [property: global::Orleans.Id(4)] ImmutableArray<NativeChecksumProfileRow> Rows);

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeChecksumProfileProtocol.RowAlias)]
internal sealed record NativeChecksumProfileRow(
    [property: global::Orleans.Id(0)] byte[] Key,
    [property: global::Orleans.Id(1)] byte[] Value);
