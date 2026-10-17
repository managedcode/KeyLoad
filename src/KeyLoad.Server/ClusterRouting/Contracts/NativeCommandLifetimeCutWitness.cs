using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCommandLifetimeCutWire.WitnessAlias)]
internal sealed record NativeCommandLifetimeCutWitness(
    [property: global::Orleans.Id(0)] NativeCommandLifetimeCutRequest Request,
    [property: global::Orleans.Id(1)] ReplicaSiloDiscovery Owner,
    [property: global::Orleans.Id(2)] long StorePosition,
    [property: global::Orleans.Id(3)] long ModelRowCount,
    [property: global::Orleans.Id(4)] long ModelReadBytes,
    [property: global::Orleans.Id(5)] string ModelDigest,
    [property: global::Orleans.Id(6)] ReadOnlyMemory<byte>? LogicalOutcome,
    [property: global::Orleans.Id(7)] ReadOnlyMemory<byte>? TechnicalOutcome);
