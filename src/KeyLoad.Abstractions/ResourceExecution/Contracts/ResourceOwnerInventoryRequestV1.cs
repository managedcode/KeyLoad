namespace KeyLoad;

/// <summary>Binds one internal owner inventory to its original scope, incarnation and deadline.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1Alias)]
internal sealed record ResourceOwnerInventoryRequestV1(
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1VersionField)] int Version,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1InventoryIdField)] Guid InventoryId,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1TenantIdField)] string TenantId,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1DatabaseIdField)] string DatabaseId,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1ResourceNameField)] string ResourceName,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1ExpectedIncarnationField)] ResourceIncarnationV1 ExpectedIncarnation,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1PageSizeField)] int PageSize,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryRequestV1ExpiresAtField)] DateTimeOffset ExpiresAt);
