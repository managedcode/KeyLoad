namespace KeyLoad;

[Orleans.GenerateSerializer, Orleans.Alias(ResourceRetirementContract.ResourceRetirementDropParentLookupAlias)]
internal sealed record ResourceRetirementDropParentLookupV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid CommandId,
    [property: Orleans.Id(2)] DropTableRequest Request);
