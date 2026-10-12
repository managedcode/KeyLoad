using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias("keyload.crashhost.controlled-movement-native-process-input.v1")]
internal sealed record ControlledPartitionMovementNativeProcessInput(
    [property: global::Orleans.Id(0)] ControlledPartitionMovementNativeHostInput Hosts,
    [property: global::Orleans.Id(1)] PartitionMovementTransportRequest Original);
