namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementCleanupCheckpointAlias)]
internal sealed record ResourceRetirementCleanupCheckpointV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] long Ordinal,
    [property: Orleans.Id(4)] int FamilyOrdinal,
    [property: Orleans.Id(5)] ReadOnlyMemory<byte> AfterKey,
    [property: Orleans.Id(6)] bool Complete,
    [property: Orleans.Id(7)] ResourceRetirementPhaseReceiptV1 Receipt)
{
    [Orleans.Id(8)]
    public long ExaminedBytes
    {
        get; init;
    }

    [Orleans.Id(9)]
    public int ExaminedRecords
    {
        get; init;
    }
    [Orleans.Id(10)]
    public ResourceRetirementOutcomeIdentityV1? OutcomeIdentity
    {
        get; init;
    }

    [Orleans.Id(ResourceRetirementCleanupAttemptContract.CheckpointAttemptId)]
    public Guid AttemptId
    {
        get; init;
    }
}
