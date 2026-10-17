namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCanonicalModelCutDiagnosticProtocol.RowAlias)]
internal sealed record NativeCanonicalModelCutRow(
    [property: global::Orleans.Id(0)] ReadOnlyMemory<byte> Key,
    [property: global::Orleans.Id(1)] ReadOnlyMemory<byte> Value);
