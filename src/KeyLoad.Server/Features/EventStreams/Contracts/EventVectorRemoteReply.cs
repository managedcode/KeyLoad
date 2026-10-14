using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.EventStreams;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(EventVectorRemoteProtocol.ReplyAlias)]
internal sealed record EventVectorRemoteReply(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] string Nonce,
    [property: global::Orleans.Id(3)] string Purpose,
    [property: global::Orleans.Id(4)] EventVectorRemoteAction Action,
    [property: global::Orleans.Id(5)] ReplicaSiloDiscovery ReceiverDiscovery,
    [property: global::Orleans.Id(6)] ReadOnlyMemory<byte> OriginalNativeReply,
    [property: global::Orleans.Id(7)] ErrorCode? Error,
    [property: global::Orleans.Id(8)] string? SafeDetail);
