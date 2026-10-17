using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.BlobStorage;

[Orleans.GenerateSerializer, Orleans.Alias(RemoteControlledBlobOutcomeProtocolV2.ReplyAlias)]
internal sealed record RemoteControlledBlobOutcomeReplyV2(
    [property: Orleans.Id(0)] Guid RequestId,
    [property: Orleans.Id(1)] string Nonce,
    [property: Orleans.Id(2)] ControlledBlobReadResult? Result,
    [property: Orleans.Id(3)] ErrorCode? Error,
    [property: Orleans.Id(4)] string? SafeDetail,
    [property: Orleans.Id(5)] ReplicaSiloDiscovery EndpointDiscovery);
