namespace KeyLoad;

/// <summary>Identifies one non-secret native owner observation and its enrollment and topology cuts.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1Alias)]
internal sealed record ResourceOwnerInventoryOwnerCutV1(
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1WriterVersionField)] int WriterVersion,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1NodeIdField)] Guid NodeId,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1StoreIncarnationField)] Guid StoreIncarnation,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1ReadGenerationField)] long ReadGeneration,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1ObservedOwnerField)] PhysicalShardRecord ObservedOwner,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1EnrollmentRevisionField)] long EnrollmentRevision,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1CatalogRevisionField)] long CatalogRevision,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1OwnerDirectoryRevisionField)] long OwnerDirectoryRevision,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1PlacementDirectoryRevisionField)] long PlacementDirectoryRevision,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1StorePositionField)] long StorePosition,
    [property: Orleans.Id(ResourceOwnerInventoryContract.ResourceOwnerInventoryOwnerCutV1AppliedIndexField)] long AppliedIndex);
