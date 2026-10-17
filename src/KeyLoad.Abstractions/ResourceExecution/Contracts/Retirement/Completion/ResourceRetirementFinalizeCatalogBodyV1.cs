namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementFinalizeCatalogBodyAlias)]
internal sealed record ResourceRetirementFinalizeCatalogBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementControlV1 Control);
