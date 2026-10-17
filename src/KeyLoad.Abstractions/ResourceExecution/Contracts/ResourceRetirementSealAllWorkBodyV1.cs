namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementSealAllWorkBodyAlias)]
internal sealed record ResourceRetirementSealAllWorkBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control);
