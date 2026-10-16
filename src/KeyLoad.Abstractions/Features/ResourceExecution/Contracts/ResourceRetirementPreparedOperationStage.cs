namespace KeyLoad;

[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPreparedOperationStageAlias)]
internal enum ResourceRetirementPreparedOperationStage : int
{
    Persisted = 1,
    Closed = 2
}
