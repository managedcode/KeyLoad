using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Exact originating command/nonce and actual receiving runtime bound to one native terminal reply.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.ReplyAlias)]
internal sealed record PartitionMovementTransportReply(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] Guid Nonce,
    [property: global::Orleans.Id(2)] PhysicalShardRecord Receiver,
    [property: global::Orleans.Id(3)] ReplicaSiloDiscovery Discovery,
    [property: global::Orleans.Id(4)] GrainOperationReply Reply);
