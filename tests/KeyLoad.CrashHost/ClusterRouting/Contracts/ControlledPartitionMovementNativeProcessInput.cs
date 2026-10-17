using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ControlledPartitionMovementNativeProcessInput.WireAlias)]
internal sealed record ControlledPartitionMovementNativeProcessInput(
    [property: global::Orleans.Id(0)] ControlledPartitionMovementNativeHostInput Hosts,
    [property: global::Orleans.Id(1)] PartitionMovementTransportRequest Original)
{
    internal const string WireAlias = "keyload.crashhost.controlled-movement-native-process-input.v1";
}
