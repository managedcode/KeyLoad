using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.BlobStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteControlledBlobProtocol.CallAlias)]
internal sealed record RemoteControlledBlobCall(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] string Nonce,
    [property: global::Orleans.Id(3)] string CallerVoter,
    [property: global::Orleans.Id(4)] string CallerSiloAddress,
    [property: global::Orleans.Id(5)] PhysicalShardRecord Source,
    [property: global::Orleans.Id(6)] RegisteredPhysicalOwnerV1 Destination,
    [property: global::Orleans.Id(7)] ControlledBlobReadRequest Request,
    [property: global::Orleans.Id(8)] int MaximumReplyBytes);
