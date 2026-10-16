namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPreparedOperationBodyAlias)]
internal sealed record ResourceRetirementPreparedOperationBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementAttemptContextV1 Context,
    [property: Orleans.Id(1)] ResourceRetirementAttemptV1 Attempt,
    [property: Orleans.Id(2)] ResourceRetirementPreparedOperationAction Action,
    [property: Orleans.Id(3)] ReplicatedOperation? Original);
