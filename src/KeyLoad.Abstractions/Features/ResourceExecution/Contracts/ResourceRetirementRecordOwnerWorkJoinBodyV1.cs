namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementRecordOwnerWorkJoinBodyAlias)]
internal sealed record ResourceRetirementRecordOwnerWorkJoinBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementWorkOwnerReceiptV1 OwnerReceipt)
{
    [Orleans.Id(2)]
    public System.Collections.Immutable.ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> DurableReads { get; init; } = [];
}
