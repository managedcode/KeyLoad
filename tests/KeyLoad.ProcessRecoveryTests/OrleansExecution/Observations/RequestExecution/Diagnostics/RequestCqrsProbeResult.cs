namespace KeyLoad.ProcessRecoveryTests.OrleansExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RequestCqrsTestProtocol.ObservationAlias)]
internal sealed record RequestCqrsProbeResult(
    [property: global::Orleans.Id(0)] bool Accepted,
    [property: global::Orleans.Id(1)] ErrorCode? Error,
    [property: global::Orleans.Id(2)] string? Subject,
    [property: global::Orleans.Id(3)] Guid RequestId,
    [property: global::Orleans.Id(4)] Guid CommandId,
    [property: global::Orleans.Id(5)] int CapabilityCalls);
