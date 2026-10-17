namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementPersistDropParentBodyAlias)]
internal sealed record ResourceRetirementPersistDropParentBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementDropParentLookupV1 Lookup,
    [property: Orleans.Id(1)] ResourceRetirementRequestV1 Expected);
