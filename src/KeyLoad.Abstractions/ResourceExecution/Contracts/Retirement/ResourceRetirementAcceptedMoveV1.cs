namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementAcceptedMoveAlias)]
internal sealed record ResourceRetirementAcceptedMoveV1(
    [property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] Guid MoveId);
