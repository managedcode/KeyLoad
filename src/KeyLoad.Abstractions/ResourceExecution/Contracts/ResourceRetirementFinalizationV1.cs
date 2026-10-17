namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementFinalizationAlias)]
internal sealed record ResourceRetirementFinalizationV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementTerminalEvidenceV1 TerminalEvidence,
    [property: Orleans.Id(2)] ResourceRetirementPhaseReceiptV1 Receipt)
{
    [Orleans.Id(3)]
    public CommitReceipt? CanonicalReceipt
    {
        get; init;
    }
    [Orleans.Id(4)]
    public ReplicatedOperation? Original
    {
        get; init;
    }
    [Orleans.Id(5)]
    public long PolicyEpoch
    {
        get; init;
    }
}
