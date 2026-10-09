using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Fresh bounded canonical control read; never an original Capture effect grant.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementProtocol.TransferAuthorityReplyAlias)]
internal sealed record PartitionMovementTransferAuthorityReply(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(4)] PhysicalShardRecord ControlOwner,
    [property: global::Orleans.Id(5)] PhysicalShardRecord SourceOwner,
    [property: global::Orleans.Id(6)] ReplicaSiloDiscovery Discovery,
    [property: global::Orleans.Id(7)] GrainOperationReply Reply);
