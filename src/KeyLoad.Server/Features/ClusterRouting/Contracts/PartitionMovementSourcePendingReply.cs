using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.SourcePendingReplyAlias)]
internal sealed record PartitionMovementSourcePendingReply(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(4)] PhysicalShardRecord ControlOwner,
    [property: global::Orleans.Id(5)] string SourceOperatorPrincipalId,
    [property: global::Orleans.Id(6)] long CurrentSourceOperatorPolicyEpoch,
    [property: global::Orleans.Id(7)] GrainOperationReply Reply,
    [property: global::Orleans.Id(8)] ReplicaSiloDiscovery Discovery);
