namespace KeyLoad.RecoveryTests;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ChecksumProfileProtocol.RowAlias)]
internal sealed record ChecksumProfileRow(
    [property: global::Orleans.Id(0)] byte[] Key,
    [property: global::Orleans.Id(1)] byte[] Value);
