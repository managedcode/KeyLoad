namespace KeyLoad;

/// <summary>Identifies the actual silo-local storage files and their database incarnation.</summary>
/// <param name="PhysicalShardId">The node identity persisted by the local store.</param>
/// <param name="Incarnation">The database incarnation persisted by the local store.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(PhysicalShardCatalogAliases.Record)]
public sealed record PhysicalShardRecord(
    [property: Orleans.Id(PhysicalShardRecordFieldIds.PhysicalShardId)] Guid PhysicalShardId,
    [property: Orleans.Id(PhysicalShardRecordFieldIds.Incarnation)] Guid Incarnation);
