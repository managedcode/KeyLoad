namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementPhaseResultAlias)]
internal sealed record ResourceRetirementPhaseResultV1(
    [property: Orleans.Id(0)] ResourceRetirementPhaseReceiptV1 Receipt,
    [property: Orleans.Id(1)] ResourceRetirementStateV1? State,
    [property: Orleans.Id(2)] ResourceRetirementOwnerFenceV1? Fence)
{
    [Orleans.Id(3)]
    public ResourceRetirementPreparedOperationV1? PreparedOperation
    {
        get; init;
    }
    [Orleans.Id(4)]
    public ResourceRetirementPreparationStateV1? PreparationState
    {
        get; init;
    }

    [Orleans.Id(5)]
    public ResourceRetirementOwnerReservationV1? OwnerReservation
    {
        get; init;
    }
    [Orleans.Id(6)]
    public ResourceRetirementWorkOwnerReceiptV1? WorkOwnerReceipt
    {
        get; init;
    }

    [Orleans.Id(7)]
    public ResourceRetirementWorkBarrierReceiptV1? WorkBarrierReceipt
    {
        get; init;
    }

    [Orleans.Id(8)]
    public ResourceRetirementCleanupCheckpointV1? CleanupCheckpoint
    {
        get; init;
    }

    [Orleans.Id(9)]
    public ResourceRetirementOwnerCleanupV1? OwnerCleanup
    {
        get; init;
    }

    [Orleans.Id(10)]
    public ResourceRetirementFinalizationV1? Finalization
    {
        get; init;
    }
    [Orleans.Id(11)]
    public ResourceRetirementOwnerReleaseV1? OwnerRelease
    {
        get; init;
    }

    [Orleans.Id(12)]
    public ResourceRetirementDropParentV1? DropParent
    {
        get; init;
    }

    [Orleans.Id(ResourceRetirementCleanupAttemptContract.ResultAttemptId)]
    public ResourceRetirementAttemptV1? CleanupAttempt
    {
        get; init;
    }
}
