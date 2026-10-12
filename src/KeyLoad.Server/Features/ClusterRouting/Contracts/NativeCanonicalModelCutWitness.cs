using System.Collections.Immutable;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCanonicalModelCutDiagnosticProtocol.WitnessAlias)]
internal sealed record NativeCanonicalModelCutWitness(
    [property: global::Orleans.Id(0)] NativeCanonicalModelCutRequest Request,
    [property: global::Orleans.Id(1)] ReplicaSiloDiscovery Owner,
    [property: global::Orleans.Id(2)] long StorePosition,
    [property: global::Orleans.Id(3)] ImmutableArray<NativeCanonicalModelCutRow> Rows,
    [property: global::Orleans.Id(4)] ReadOnlyMemory<byte>? LogicalOutcome,
    [property: global::Orleans.Id(5)] ReadOnlyMemory<byte>? TechnicalOutcome,
    [property: global::Orleans.Id(6)] ReadOnlyMemory<byte>? TargetReceipt,
    [property: global::Orleans.Id(7)] ReadOnlyMemory<byte>? TargetCounter,
    [property: global::Orleans.Id(8)] ReadOnlyMemory<byte>? SourceIntent,
    [property: global::Orleans.Id(9)] ReadOnlyMemory<byte>? SourceCounter);
