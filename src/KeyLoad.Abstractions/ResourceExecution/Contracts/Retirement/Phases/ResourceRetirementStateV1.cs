using System.Collections.Immutable;

namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementStateAlias)]
internal sealed record ResourceRetirementStateV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] ResourceRetirementStage Stage,
    [property: Orleans.Id(2)] ImmutableArray<ResourceRetirementPhaseReceiptV1> OwnerReceipts,
    [property: Orleans.Id(3)] ResourceOwnerInventoryResultV1? FinalInventory,
    [property: Orleans.Id(4)] ResourceRetirementPhaseReceiptV1? SealedReceipt)
{
    [Orleans.Id(5)]
    public ResourceRetirementAttemptV1? SealAttempt
    {
        get; init;
    }
    [Orleans.Id(6)]
    public ImmutableArray<ResourceRetirementWorkOwnerReceiptV1> WorkOwnerReceipts { get; init; } = [];

    [Orleans.Id(7)]
    public ResourceRetirementWorkBarrierReceiptV1? WorkBarrierReceipt
    {
        get; init;
    }
    [Orleans.Id(8)]
    public ImmutableArray<ResourceRetirementOwnerCleanupV1> OwnerCleanupReceipts { get; init; } = [];

    [Orleans.Id(9)]
    public ResourceRetirementFinalizationV1? Finalization
    {
        get; init;
    }

    [Orleans.Id(10)]
    public ImmutableArray<ResourceRetirementOwnerReleaseV1> OwnerReleaseReceipts { get; init; } = [];

    [Orleans.Id(11)]
    public ResourceRetirementPhaseReceiptV1? CompletionReceipt
    {
        get; init;
    }
    [Orleans.Id(12)]
    public ReplicatedOperation? CompletionOperation
    {
        get; init;
    }

    [Orleans.Id(13)]
    public long? CompletionPolicyEpoch
    {
        get; init;
    }
}
