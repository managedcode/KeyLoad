namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Carries the exact typed Core command or query bytes to one original native owner generation.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceRetirementNativeProtocol.RequestAlias)]
internal sealed record ResourceRetirementNativeRequest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid RequestId,
    [property: global::Orleans.Id(2)] Guid Nonce,
    [property: global::Orleans.Id(3)] string PrincipalId,
    [property: global::Orleans.Id(4)] long PolicyEpoch,
    [property: global::Orleans.Id(5)] RegisteredPhysicalOwnerV1 Control,
    [property: global::Orleans.Id(6)] RegisteredPhysicalOwnerV1 Destination,
    [property: global::Orleans.Id(7)] string CallerVoter,
    [property: global::Orleans.Id(8)] string CallerSiloAddress,
    [property: global::Orleans.Id(9)] string DestinationVoter,
    [property: global::Orleans.Id(10)] string DestinationSiloAddress,
    [property: global::Orleans.Id(11)] DateTimeOffset IssuedAt,
    [property: global::Orleans.Id(12)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(13)] long MaximumReadBytes,
    [property: global::Orleans.Id(14)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(15)] int MaximumResultBytes,
    [property: global::Orleans.Id(16)] ReadOnlyMemory<byte> Payload,
    [property: global::Orleans.Id(17)] string Signature,
    [property: global::Orleans.Id(18)] Guid CommandId,
    [property: global::Orleans.Id(19)] bool Command);
