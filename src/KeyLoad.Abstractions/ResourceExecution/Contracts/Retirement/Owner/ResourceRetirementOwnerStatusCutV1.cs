namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementOwnerStatusCutAlias)]
internal sealed record ResourceRetirementOwnerStatusCutV1(
    [property: Orleans.Id(0)] Guid NodeId,
    [property: Orleans.Id(1)] Guid StoreIncarnation,
    [property: Orleans.Id(2)] long ReadGeneration,
    [property: Orleans.Id(3)] long StorePosition,
    [property: Orleans.Id(4)] long AppliedIndex,
    [property: Orleans.Id(5)] long CatalogRevision);
