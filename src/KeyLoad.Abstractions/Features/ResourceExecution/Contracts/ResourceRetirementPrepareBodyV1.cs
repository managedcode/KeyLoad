namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementPrepareBodyAlias)]
internal sealed record ResourceRetirementPrepareBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementRequestV1 Request);
