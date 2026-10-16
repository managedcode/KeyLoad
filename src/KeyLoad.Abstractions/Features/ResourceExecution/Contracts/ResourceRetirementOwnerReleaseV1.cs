namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementOwnerReleaseAlias)]
internal sealed record ResourceRetirementOwnerReleaseV1(
    [property: Orleans.Id(0)] ResourceRetirementFinalizationV1 Finalization,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver,
    [property: Orleans.Id(2)] ResourceRetirementPhaseReceiptV1 Receipt)
{
    [Orleans.Id(3)]
    public long PreparedReplacementCount
    {
        get; init;
    }
    [Orleans.Id(4)]
    public ResourceRetirementOutcomeIdentityV1? OutcomeIdentity
    {
        get; init;
    }
}
