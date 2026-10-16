namespace KeyLoad.Server.Features.DocumentStorage;

[Orleans.GenerateSerializer, Orleans.Alias(RemoteResourceLifetimeReadProtocol.CallAlias)]
internal sealed record RemoteResourceLifetimeReadCallV2(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] RemoteResourceLifetimeReadKindV2 Kind,
    [property: Orleans.Id(2)] Guid RequestId,
    [property: Orleans.Id(3)] string Nonce,
    [property: Orleans.Id(4)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(5)] string CallerVoter,
    [property: Orleans.Id(6)] string CallerSiloAddress,
    [property: Orleans.Id(7)] PhysicalShardRecord Source,
    [property: Orleans.Id(8)] RegisteredPhysicalOwnerV1 Destination,
    [property: Orleans.Id(9)] ReadOnlyMemory<byte> Payload,
    [property: Orleans.Id(10)] int MaximumReplyBytes);
