namespace KeyLoad.Server.Features.ResourceExecution;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceRetirementNativeProtocol.PhaseAlias)]
internal sealed record ResourceRetirementNativePhaseRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] ResourceRetirementPhaseCommandV1 Phase,
    [property: global::Orleans.Id(2)] RegisteredPhysicalOwnerV1 ObservedOwner,
    [property: global::Orleans.Id(3)] string DestinationVoter,
    [property: global::Orleans.Id(4)] string DestinationSiloAddress,
    [property: global::Orleans.Id(5)] long OriginalPolicyEpoch,
    [property: global::Orleans.Id(6)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(7)] long MaximumReadBytes,
    [property: global::Orleans.Id(8)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(9)] int MaximumResultBytes)
{
    [global::Orleans.Id(10)]
    public ReplicatedOperation? OriginalOperation
    {
        get; init;
    }
}

