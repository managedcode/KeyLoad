namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementAttemptContextAlias)]
internal sealed record ResourceRetirementAttemptContextV1(
    [property: Orleans.Id(0)] ResourceRetirementAttemptPurpose Purpose,
    [property: Orleans.Id(1)] ResourceRetirementControlV1? Retirement,
    [property: Orleans.Id(2)] ResourceRetirementPreparationControlV1? Preparation,
    [property: Orleans.Id(3)] RegisteredPhysicalOwnerV1? Receiver)
{
    [Orleans.Id(ResourceRetirementCleanupAttemptContract.ContextPageId)]
    public ResourceRetirementCleanupPageCoordinateV1? Page
    {
        get; init;
    }
}
