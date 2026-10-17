namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementFinishBodyAlias)]
internal sealed record ResourceRetirementFinishBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control);
