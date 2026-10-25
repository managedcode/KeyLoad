using System.Collections.Immutable;

namespace KeyLoad.ProcessRecoveryTests;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ChecksumProfileProtocol.CutAlias)]
internal sealed record ChecksumProfileCut(
    [property: global::Orleans.Id(0)] Guid NodeId,
    [property: global::Orleans.Id(1)] Guid Incarnation,
    [property: global::Orleans.Id(2)] long Position,
    [property: global::Orleans.Id(3)] long ReadGeneration,
    [property: global::Orleans.Id(4)] ImmutableArray<ChecksumProfileRow> Rows);
