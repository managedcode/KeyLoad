using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(CommandObservationNativeProtocol.RequestAlias)]
internal sealed record CommandObservationNativeRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] CommandObservationNativeAction Action,
    [property: global::Orleans.Id(2)] string OpaqueToken,
    [property: global::Orleans.Id(3)] ReplicaSiloDiscovery Caller,
    [property: global::Orleans.Id(4)] ReplicaSiloDiscovery Destination,
    [property: global::Orleans.Id(5)] Guid Nonce,
    [property: global::Orleans.Id(6)] string Signature);
