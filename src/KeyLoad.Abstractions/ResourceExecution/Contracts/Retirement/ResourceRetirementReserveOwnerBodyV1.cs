namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementReserveOwnerBodyAlias)]
internal sealed record ResourceRetirementReserveOwnerBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementAttemptContextV1 Context,
    [property: Orleans.Id(1)] ResourceRetirementAttemptV1 Attempt);
