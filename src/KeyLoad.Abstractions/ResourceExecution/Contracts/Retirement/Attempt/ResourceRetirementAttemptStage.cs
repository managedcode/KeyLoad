namespace KeyLoad;

[Orleans.Alias(ResourceRetirementContract.ResourceRetirementAttemptStageAlias)]
internal enum ResourceRetirementAttemptStage : int
{
    Prepared = 1,
    Failed = 2,
    Expired = 3,
    Completed = 4
}
