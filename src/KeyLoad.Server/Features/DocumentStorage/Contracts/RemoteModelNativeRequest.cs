namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>Original model bytes bound to both physical tuples and actual silo generations.</summary>
[GenerateSerializer, Alias(RemoteModelNativeProtocol.RequestAlias)]
internal sealed record RemoteModelNativeRequest(
    [property: Id(0)] RemoteModelNativeChannel Channel,
    [property: Id(1)] ReadOnlyMemory<byte> Payload,
    [property: Id(2)] string Signature,
    [property: Id(3)] PhysicalShardRecord SourceOwner,
    [property: Id(4)] PhysicalShardRecord DestinationOwner,
    [property: Id(5)] string CallerVoter,
    [property: Id(6)] string CallerSiloAddress,
    [property: Id(7)] string DestinationVoter,
    [property: Id(8)] string DestinationSiloAddress,
    [property: Id(9)] Guid Nonce,
    [property: Id(10)] DateTimeOffset ExpiresAt,
    [property: Id(11)] string SenderSignature);
