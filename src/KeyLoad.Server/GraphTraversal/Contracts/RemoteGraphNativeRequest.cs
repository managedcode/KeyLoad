namespace KeyLoad.Server.Features.GraphTraversal;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphNativeProtocol.RequestAlias)]
internal sealed record RemoteGraphNativeRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] RemoteGraphNativeAction Action,
    [property: global::Orleans.Id(3)] string PrincipalId,
    [property: global::Orleans.Id(4)] RegisteredPhysicalOwnerV1 SourceOwner,
    [property: global::Orleans.Id(5)] RegisteredPhysicalOwnerV1 DestinationOwner,
    [property: global::Orleans.Id(6)] string CallerVoter,
    [property: global::Orleans.Id(7)] string CallerSiloAddress,
    [property: global::Orleans.Id(8)] string DestinationVoter,
    [property: global::Orleans.Id(9)] string DestinationSiloAddress,
    [property: global::Orleans.Id(10)] Guid Nonce,
    [property: global::Orleans.Id(11)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(12)] ReadOnlyMemory<byte> Payload,
    [property: global::Orleans.Id(13)] string Signature);
