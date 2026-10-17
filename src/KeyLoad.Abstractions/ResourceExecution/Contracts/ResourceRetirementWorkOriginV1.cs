namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceRetirementContract.ResourceRetirementWorkOriginAlias)]
internal sealed record ResourceRetirementWorkOriginV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PhysicalShardRecord PhysicalOwner,
    [property: Orleans.Id(2)] string VoterId,
    [property: Orleans.Id(3)] string FullSiloAddress,
    [property: Orleans.Id(4)] Guid StoreNodeId,
    [property: Orleans.Id(5)] Guid StoreIncarnation,
    [property: Orleans.Id(6)] long ReadGeneration);
