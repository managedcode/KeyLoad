using System.Collections.Immutable;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ControlledPartitionMovementNativeProcessResult.WireAlias)]
internal sealed record ControlledPartitionMovementNativeProcessResult(
    [property: global::Orleans.Id(0)] ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> Before,
    [property: global::Orleans.Id(1)] ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> After,
    [property: global::Orleans.Id(2)] ReadOnlyMemory<byte> OriginalReply,
    [property: global::Orleans.Id(3)] string Signature)
{
    internal const string WireAlias = "keyload.crashhost.controlled-movement-native-process-result.v1";
}
