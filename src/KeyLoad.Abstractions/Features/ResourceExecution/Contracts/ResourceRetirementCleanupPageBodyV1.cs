namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementCleanupPageBodyAlias)]
internal sealed record ResourceRetirementCleanupPageBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementStateV1 SealedState,
    [property: Orleans.Id(1)] RegisteredPhysicalOwnerV1 Receiver,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] ResourceRetirementCleanupCheckpointV1? Previous)
{
    [Orleans.Id(4)]
    public long MaximumReadBytes
    {
        get; init;
    }

    [Orleans.Id(5)]
    public int MaximumExaminedRecords
    {
        get; init;
    }

    [Orleans.Id(ResourceRetirementCleanupAttemptContract.BodyAttemptId)]
    public ResourceRetirementAttemptV1? Attempt
    {
        get; init;
    }
}
