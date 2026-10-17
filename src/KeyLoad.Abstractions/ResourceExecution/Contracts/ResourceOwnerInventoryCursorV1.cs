namespace KeyLoad;

/// <summary>Continues a bounded owner scan under its exact original authenticated observation.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1Alias)]
internal sealed record ResourceOwnerInventoryCursorV1(
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1InventoryIdField)] Guid InventoryId,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1IncarnationField)] ResourceIncarnationV1 Incarnation,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1CutField)] ResourceOwnerInventoryOwnerCutV1 Cut,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1AfterKeyField)] ReadOnlyMemory<byte> AfterKey,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1PrincipalIdField)] string PrincipalId,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1PolicyEpochField)] long PolicyEpoch,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryCursorV1ExpiresAtField)] DateTimeOffset ExpiresAt);
