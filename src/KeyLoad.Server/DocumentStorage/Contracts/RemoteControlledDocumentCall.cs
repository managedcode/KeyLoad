using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.DocumentStorage;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteDocumentProtocol.ControlledCallAlias)]
internal sealed record RemoteControlledDocumentCall(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] string Nonce,
    [property: global::Orleans.Id(3)] string CallerVoter,
    [property: global::Orleans.Id(4)] string CallerSiloAddress,
    [property: global::Orleans.Id(5)] PhysicalShardRecord Source,
    [property: global::Orleans.Id(6)] RegisteredPhysicalOwnerV1 Destination,
    [property: global::Orleans.Id(7)] ControlledDocumentReadRequest Request,
    [property: global::Orleans.Id(8)] int MaximumReplyBytes);
