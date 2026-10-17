using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphNativeProtocol.ReplyAlias)]
internal sealed record RemoteGraphNativeReplyV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] ReadOnlyMemory<byte> Payload,
    [property: global::Orleans.Id(4)] ErrorCode? Error,
    [property: global::Orleans.Id(5)] string? SafeDetail,
    [property: global::Orleans.Id(6)] ReplicaSiloDiscovery ReceiverDiscovery,
    [property: global::Orleans.Id(7)] string Signature);
