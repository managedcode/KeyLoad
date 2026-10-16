namespace KeyLoad.Clustering;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceRetirementReadContract.RequestAlias)]
internal sealed record ResourceRetirementReadRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] ResourceRetirementReadOperation Operation,
    [property: global::Orleans.Id(2)] ResourceRetirementRequestV1? Request,
    [property: global::Orleans.Id(3)] ResourceRetirementControlV1? Control,
    [property: global::Orleans.Id(4)] Guid InventoryId,
    [property: global::Orleans.Id(5)] int PageSize,
    [property: global::Orleans.Id(6)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(7)] RegisteredPhysicalOwnerV1 ObservedOwner,
    [property: global::Orleans.Id(8)] string DestinationVoter,
    [property: global::Orleans.Id(9)] string DestinationSiloAddress,
    [property: global::Orleans.Id(10)] long OriginalPolicyEpoch,
    [property: global::Orleans.Id(11)] long MaximumReadBytes,
    [property: global::Orleans.Id(12)] int MaximumExaminedRecords,
    [property: global::Orleans.Id(13)] int MaximumResultBytes)
{
    [global::Orleans.Id(14)]
    public ResourceRetirementAttemptV1? Attempt
    {
        get; init;
    }

    [global::Orleans.Id(15)]
    public Guid AttemptId
    {
        get; init;
    }

    [global::Orleans.Id(16)]
    public ResourceRetirementPreparationControlV1? Preparation
    {
        get; init;
    }

    [global::Orleans.Id(17)]
    public ResourceRetirementAttemptContextV1? Context
    {
        get; init;
    }

    [global::Orleans.Id(18)]
    public ResourceRetirementOwnerFenceV1? WorkFence
    {
        get; init;
    }

    [global::Orleans.Id(19)]
    public ResourceRetirementWorkOwnerReceiptV1? AcknowledgedWork
    {
        get; init;
    }
    [global::Orleans.Id(23)]
    public ResourceRetirementCleanupPageBodyV1? CleanupPage
    {
        get; init;
    }

    [global::Orleans.Id(22)]
    public PartitionRef? CleanupPartition
    {
        get; init;
    }

    [global::Orleans.Id(21)]
    public ResourceRetirementSealBodyV1? SourceSeal
    {
        get; init;
    }

    [global::Orleans.Id(20)]
    public ResourceRetirementDropParentLookupV1? ParentLookup
    {
        get; init;
    }
}
