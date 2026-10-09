using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.TransferDataReplyAlias)]
internal sealed record PartitionMovementTransferDataReply(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] Guid Nonce,
    [property: global::Orleans.Id(2)] PhysicalShardRecord SourceOwner,
    [property: global::Orleans.Id(3)] ReplicaSiloDiscovery Discovery,
    [property: global::Orleans.Id(4)] GrainOperationReply Reply);
