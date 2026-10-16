namespace KeyLoad;

/// <summary>Names the frozen internal resource owner inventory native contracts and fields.</summary>
internal static class ResourceOwnerInventoryContract
{
    internal const string CoverageKindAlias = "keyload.resource-owner-inventory.coverage-kind.v1";

    internal const string ResourceOwnerInventoryRequestV1Alias = "keyload.resource-owner-inventory.request.v1";
    internal const int ResourceOwnerInventoryRequestV1VersionField = 0;
    internal const int ResourceOwnerInventoryRequestV1InventoryIdField = 1;
    internal const int ResourceOwnerInventoryRequestV1TenantIdField = 2;
    internal const int ResourceOwnerInventoryRequestV1DatabaseIdField = 3;
    internal const int ResourceOwnerInventoryRequestV1ResourceNameField = 4;
    internal const int ResourceOwnerInventoryRequestV1ExpectedIncarnationField = 5;
    internal const int ResourceOwnerInventoryRequestV1PageSizeField = 6;
    internal const int ResourceOwnerInventoryRequestV1ExpiresAtField = 7;

    internal const string ResourceOwnerInventoryControlV1Alias = "keyload.resource-owner-inventory.control.v1";
    internal const int ResourceOwnerInventoryControlV1RequestField = 0;
    internal const int ResourceOwnerInventoryControlV1DefinitionField = 1;
    internal const int ResourceOwnerInventoryControlV1LifetimeField = 2;
    internal const int ResourceOwnerInventoryControlV1CatalogField = 3;
    internal const int ResourceOwnerInventoryControlV1DirectoryField = 4;
    internal const int ResourceOwnerInventoryControlV1PlacementDirectoryRevisionField = 5;
    internal const int ResourceOwnerInventoryControlV1PrincipalIdField = 6;
    internal const int ResourceOwnerInventoryControlV1PolicyEpochField = 7;

    internal const string ResourceOwnerInventoryOwnerCutV1Alias = "keyload.resource-owner-inventory.owner-cut.v1";
    internal const int ResourceOwnerInventoryOwnerCutV1WriterVersionField = 0;
    internal const int ResourceOwnerInventoryOwnerCutV1NodeIdField = 1;
    internal const int ResourceOwnerInventoryOwnerCutV1StoreIncarnationField = 2;
    internal const int ResourceOwnerInventoryOwnerCutV1ReadGenerationField = 3;
    internal const int ResourceOwnerInventoryOwnerCutV1ObservedOwnerField = 4;
    internal const int ResourceOwnerInventoryOwnerCutV1EnrollmentRevisionField = 5;
    internal const int ResourceOwnerInventoryOwnerCutV1CatalogRevisionField = 6;
    internal const int ResourceOwnerInventoryOwnerCutV1OwnerDirectoryRevisionField = 7;
    internal const int ResourceOwnerInventoryOwnerCutV1PlacementDirectoryRevisionField = 8;
    internal const int ResourceOwnerInventoryOwnerCutV1StorePositionField = 9;
    internal const int ResourceOwnerInventoryOwnerCutV1AppliedIndexField = 10;

    internal const string ResourceOwnerInventoryCursorV1Alias = "keyload.resource-owner-inventory.cursor.v1";
    internal const int ResourceOwnerInventoryCursorV1InventoryIdField = 0;
    internal const int ResourceOwnerInventoryCursorV1IncarnationField = 1;
    internal const int ResourceOwnerInventoryCursorV1CutField = 2;
    internal const int ResourceOwnerInventoryCursorV1AfterKeyField = 3;
    internal const int ResourceOwnerInventoryCursorV1PrincipalIdField = 4;
    internal const int ResourceOwnerInventoryCursorV1PolicyEpochField = 5;
    internal const int ResourceOwnerInventoryCursorV1ExpiresAtField = 6;

    internal const string ResourceOwnerInventoryEntryV1Alias = "keyload.resource-owner-inventory.entry.v1";
    internal const int ResourceOwnerInventoryEntryV1LifetimeField = 0;
    internal const int ResourceOwnerInventoryEntryV1PartitionField = 1;
    internal const int ResourceOwnerInventoryEntryV1ObservedOwnerField = 2;
    internal const int ResourceOwnerInventoryEntryV1CurrentPlacementField = 3;
    internal const int ResourceOwnerInventoryEntryV1CurrentRouteField = 4;

    internal const string ResourceOwnerInventoryPageV1Alias = "keyload.resource-owner-inventory.page.v1";
    internal const int ResourceOwnerInventoryPageV1CutField = 0;
    internal const int ResourceOwnerInventoryPageV1EntriesField = 1;
    internal const int ResourceOwnerInventoryPageV1ContinuationField = 2;

    internal const string ResourceOwnerInventoryResultV1Alias = "keyload.resource-owner-inventory.result.v1";
    internal const int ResourceOwnerInventoryResultV1ControlField = 0;
    internal const int ResourceOwnerInventoryResultV1OwnerCutsField = 1;
    internal const int ResourceOwnerInventoryResultV1EntriesField = 2;
    internal const int ResourceOwnerInventoryResultV1CompleteField = 3;
    internal const int ResourceOwnerInventoryResultV1CoverageKindField = 4;
}
