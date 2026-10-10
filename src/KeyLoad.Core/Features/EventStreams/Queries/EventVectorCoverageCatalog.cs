namespace KeyLoad.Core;

internal sealed record EventVectorCoverageCatalog(
    PhysicalShardCatalog Catalog,
    PhysicalOwnerDirectoryV1 Owners,
    EventVectorCoverageRow CatalogRow,
    EventVectorCoverageRow OwnersRow);
