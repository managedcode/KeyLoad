namespace KeyLoad;

/// <summary>Retains the authenticated source resource and topology witnesses.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1Alias)]
internal sealed record ResourceOwnerInventoryControlV1(
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1RequestField)] ResourceOwnerInventoryRequestV1 Request,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1DefinitionField)] ResourceDefinition Definition,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1LifetimeField)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1CatalogField)] PhysicalShardCatalog Catalog,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1DirectoryField)] PhysicalOwnerDirectoryV1 Directory,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1PlacementDirectoryRevisionField)] long PlacementDirectoryRevision,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1PrincipalIdField)] string PrincipalId,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryControlV1PolicyEpochField)] long PolicyEpoch);
