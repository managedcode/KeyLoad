namespace KeyLoad;

[Orleans.Alias(ResourceRetirementContract.ResourceRetirementStageAlias)]
internal enum ResourceRetirementStage : int
{
    Retiring = 1,
    BarrierSealed = 2,
    CatalogFinalized = 3,
    Completed = 4
}
