using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementWorkBarrierReceiptAlias)]
internal sealed record ResourceRetirementWorkBarrierReceiptV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementPhaseReceiptV1 SealedReceipt,
    [property: Orleans.Id(2)] ImmutableArray<ResourceRetirementWorkOwnerReceiptV1> Owners,
    [property: Orleans.Id(3)] ResourceRetirementPhaseReceiptV1 Receipt)
{
    [Orleans.Id(4)]
    public ReplicatedOperation? Original
    {
        get; init;
    }

    [Orleans.Id(5)]
    public long? PolicyEpoch
    {
        get; init;
    }
}
