using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ControlledPartitionMovementNativeProcessSnapshot.WireAlias)]
internal sealed record ControlledPartitionMovementNativeProcessSnapshot(
    [property: global::Orleans.Id(0)] StoreIdentity Identity,
    [property: global::Orleans.Id(1)] long Position,
    [property: global::Orleans.Id(2)] ReplicaHardState State,
    [property: global::Orleans.Id(3)] ImmutableArray<string> Rows,
    [property: global::Orleans.Id(4)] ImmutableArray<ReplicaEntry> Entries)
{
    internal const string WireAlias = "keyload.crashhost.controlled-movement-native-process-snapshot.v1";
}
