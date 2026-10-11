namespace KeyLoad.Server.Features.EventStreams;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(EventVectorRemoteProtocol.CallAlias)]
internal sealed record EventVectorRemoteCall(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] string Nonce,
    [property: global::Orleans.Id(3)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(4)] string CallerVoter,
    [property: global::Orleans.Id(5)] string CallerSiloAddress,
    [property: global::Orleans.Id(6)] PhysicalShardRecord Source,
    [property: global::Orleans.Id(7)] string Purpose,
    [property: global::Orleans.Id(8)] EventVectorRemoteAction Action,
    [property: global::Orleans.Id(9)] ReadOnlyMemory<byte> OriginalNativeRequest);
