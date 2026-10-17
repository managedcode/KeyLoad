namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceRetirementReadContract.ReplyAlias)]
internal sealed record ResourceRetirementReadReplyV1(
    [property: global::Orleans.Id(0)] ResourceRetirementStateV1? State,
    [property: global::Orleans.Id(1)] ResourceRetirementOwnerFenceV1? Fence,
    [property: global::Orleans.Id(2)] ResourceOwnerInventoryControlV1? InventoryControl,
    [property: global::Orleans.Id(3)] long ReadBytes,
    [property: global::Orleans.Id(4)] int ExaminedRecords)
{
    [global::Orleans.Id(5)]
    public ResourceRetirementAttemptV1? Attempt
    {
        get; init;
    }

    [global::Orleans.Id(6)]
    public ResourceRetirementPreparedOperationV1? PreparedOperation
    {
        get; init;
    }

    [global::Orleans.Id(7)]
    public ResourceRetirementPreparationStateV1? Preparation
    {
        get; init;
    }

    [global::Orleans.Id(8)]
    public ResourceRetirementOwnerStatusV1? OwnerStatus
    {
        get; init;
    }

    [global::Orleans.Id(9)]
    public ReplicatedOperation? OriginalOperation
    {
        get; init;
    }

    [global::Orleans.Id(10)]
    public ResourceRetirementWorkJoinWitnessV1? WorkJoinWitness
    {
        get; init;
    }

    [global::Orleans.Id(11)]
    public ResourceRetirementWorkOwnerReceiptV1? AcknowledgedOwner
    {
        get; init;
    }
    [global::Orleans.Id(14)]
    public ResourceRetirementCleanupCheckpointV1? CleanupCheckpoint
    {
        get; init;
    }

    [global::Orleans.Id(15)]
    public ResourceRetirementOwnerCleanupV1? OwnerCleanup
    {
        get; init;
    }

    [global::Orleans.Id(16)]
    public ResourceRetirementOwnerReleaseV1? OwnerRelease
    {
        get; init;
    }

    [global::Orleans.Id(13)]
    public ResourceRetirementRequestV1? AdmittedDrop
    {
        get; init;
    }

    [global::Orleans.Id(12)]
    public ResourceRetirementDropParentV1? Parent
    {
        get; init;
    }
}
