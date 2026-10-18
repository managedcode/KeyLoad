namespace KeyLoad.RecoveryHost;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeChecksumProfileProtocol.RowAlias)]
internal sealed record NativeChecksumProfileRow(
    [property: global::Orleans.Id(0)] byte[] Key,
    [property: global::Orleans.Id(1)] byte[] Value);
