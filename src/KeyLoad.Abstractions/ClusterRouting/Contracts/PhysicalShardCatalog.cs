namespace KeyLoad;

/// <summary>The committed physical placement identity for the default shard.</summary>
/// <param name="Version">The catalog format version.</param>
/// <param name="Revision">The monotonic catalog revision.</param>
/// <param name="DefaultShard">The current default physical shard record.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(PhysicalShardCatalogAliases.Catalog)]
public sealed record PhysicalShardCatalog(
    [property: Orleans.Id(PhysicalShardCatalogFieldIds.Version)] int Version,
    [property: Orleans.Id(PhysicalShardCatalogFieldIds.Revision)] long Revision,
    [property: Orleans.Id(PhysicalShardCatalogFieldIds.DefaultShard)] PhysicalShardRecord DefaultShard);
