namespace KeyLoad;

/// <summary>Keeps one conservative potential resource membership, not a nonempty-data census, distinct from current routing.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryContract.ResourceOwnerInventoryEntryV1Alias)]
internal sealed record ResourceOwnerInventoryEntryV1(
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryEntryV1LifetimeField)] ResourceLifetimeV1 Lifetime,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryEntryV1PartitionField)] PartitionRef Partition,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryEntryV1ObservedOwnerField)] PhysicalShardRecord ObservedOwner,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryEntryV1CurrentPlacementField)] AtomicPartitionPlacementResolution CurrentPlacement,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryEntryV1CurrentRouteField)] RegisteredPhysicalOwnerV1 CurrentRoute);
