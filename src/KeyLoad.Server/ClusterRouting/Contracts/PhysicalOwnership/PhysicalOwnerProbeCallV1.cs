namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PhysicalOwnerProbeProtocol.CallAlias)]
internal sealed record PhysicalOwnerProbeCallV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] RegisteredPhysicalOwnerV1 Control,
    [property: global::Orleans.Id(3)] RegisteredPhysicalOwnerV1 Destination,
    [property: global::Orleans.Id(4)] string CallerVoter,
    [property: global::Orleans.Id(5)] string CallerSiloAddress,
    [property: global::Orleans.Id(6)] string Nonce,
    [property: global::Orleans.Id(7)] DateTimeOffset IssuedAt,
    [property: global::Orleans.Id(8)] DateTimeOffset ExpiresAt);
