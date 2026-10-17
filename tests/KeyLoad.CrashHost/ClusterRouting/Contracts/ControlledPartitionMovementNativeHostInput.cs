using System.Collections.Immutable;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledPartitionMovementNativeHostInput.WireAlias)]
internal sealed record ControlledPartitionMovementNativeHostInput(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] ImmutableArray<ImmutableArray<string>> Arguments,
    [property: global::Orleans.Id(2)] int PrimarySiloPort,
    [property: global::Orleans.Id(3)] string Root,
    [property: global::Orleans.Id(4)] RegisteredPhysicalOwnerV1 Control,
    [property: global::Orleans.Id(5)] RegisteredPhysicalOwnerV1 Destination)
{
    internal const string WireAlias = "keyload.crashhost.controlled-movement-native-host-input.v1";
}
