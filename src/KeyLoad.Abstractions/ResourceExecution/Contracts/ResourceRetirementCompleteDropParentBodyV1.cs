namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementCompleteDropParentBodyAlias)]
internal sealed record ResourceRetirementCompleteDropParentBodyV1(
    [property: Orleans.Id(0)] ResourceRetirementDropParentLookupV1 Lookup);
