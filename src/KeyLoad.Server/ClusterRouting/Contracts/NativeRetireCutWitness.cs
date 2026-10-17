using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeRetireCutWire.WitnessAlias)]
internal sealed record NativeRetireCutWitness(
    [property: global::Orleans.Id(0)] NativeRetireCutRequest Request,
    [property: global::Orleans.Id(1)] ReplicaSiloDiscovery Owner,
    [property: global::Orleans.Id(2)] long Position,
    [property: global::Orleans.Id(3)] long ReplicaTail,
    [property: global::Orleans.Id(4)] StoreIdentity Identity,
    [property: global::Orleans.Id(5)] string SigningKeySha256,
    [property: global::Orleans.Id(6)] ImmutableArray<NativeCanonicalModelCutRow> Rows,
    [property: global::Orleans.Id(7)] ReadOnlyMemory<byte>? OriginalSealedOperation,
    [property: global::Orleans.Id(8)] ReplicaEntry? ReceiptEntry,
    [property: global::Orleans.Id(9)] long CommittedIndex);
