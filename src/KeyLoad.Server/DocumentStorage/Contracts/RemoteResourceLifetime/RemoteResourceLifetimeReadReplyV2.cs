using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.DocumentStorage;

[Orleans.GenerateSerializer, Orleans.Alias(RemoteResourceLifetimeReadProtocol.ReplyAlias)]
internal sealed record RemoteResourceLifetimeReadReplyV2(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] RemoteResourceLifetimeReadKindV2 Kind,
    [property: Orleans.Id(2)] Guid RequestId,
    [property: Orleans.Id(3)] string Nonce,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> Payload,
    [property: Orleans.Id(5)] ErrorCode? Error,
    [property: Orleans.Id(6)] string? SafeDetail,
    [property: Orleans.Id(7)] SiloStorageIdentity EndpointDiscovery);
