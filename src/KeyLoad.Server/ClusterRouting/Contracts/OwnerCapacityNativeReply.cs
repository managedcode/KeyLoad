using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(OwnerCapacityNativeReplyAliases.OwnerCapacityNativeReplyAlias)]
internal sealed record OwnerCapacityNativeReply(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] OwnerCapacitySnapshotV1? Snapshot,
    [property: global::Orleans.Id(4)] ErrorCode? Error,
    [property: global::Orleans.Id(5)] string? SafeDetail,
    [property: global::Orleans.Id(6)] string Signature);
