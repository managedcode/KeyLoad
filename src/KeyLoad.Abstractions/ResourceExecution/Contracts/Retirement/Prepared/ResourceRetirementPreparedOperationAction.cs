namespace KeyLoad;

[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPreparedOperationActionAlias)]
internal enum ResourceRetirementPreparedOperationAction : int
{
    Persist = 1,
    Close = 2
}
