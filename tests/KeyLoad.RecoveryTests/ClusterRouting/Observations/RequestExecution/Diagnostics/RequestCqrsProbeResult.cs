namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsTestProtocol.ObservationAlias)]
internal sealed record RequestCqrsProbeResult(
    [property: global::Orleans.Id(0)] bool Accepted,
    [property: global::Orleans.Id(1)] ErrorCode? Error,
    [property: global::Orleans.Id(2)] string? Subject,
    [property: global::Orleans.Id(3)] Guid RequestId,
    [property: global::Orleans.Id(4)] Guid CommandId,
    [property: global::Orleans.Id(5)] long PrincipalBytes,
    [property: global::Orleans.Id(6)] long StateBytes,
    [property: global::Orleans.Id(7)] long StartedChunkBytes,
    [property: global::Orleans.Id(8)] int CapabilityCalls);
