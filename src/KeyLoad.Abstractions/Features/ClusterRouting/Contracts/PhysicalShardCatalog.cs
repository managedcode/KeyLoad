using System.Collections.Immutable;

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

/// <summary>The stable identity and current ownership epoch of one physical shard.</summary>
/// <param name="PhysicalShardId">The independently configured opaque shard identity.</param>
/// <param name="Incarnation">The replica-group incarnation associated with the placement.</param>
/// <param name="VoterIds">The ordered voter identities in the configured cohort.</param>
/// <param name="PlacementEpoch">The monotonic physical placement epoch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(PhysicalShardCatalogAliases.Record)]
public sealed record PhysicalShardRecord(
    [property: Orleans.Id(PhysicalShardRecordFieldIds.PhysicalShardId)] Guid PhysicalShardId,
    [property: Orleans.Id(PhysicalShardRecordFieldIds.Incarnation)] Guid Incarnation,
    [property: Orleans.Id(PhysicalShardRecordFieldIds.VoterIds)] ImmutableArray<string> VoterIds,
    [property: Orleans.Id(PhysicalShardRecordFieldIds.PlacementEpoch)] long PlacementEpoch);

/// <summary>Requests the initial catalog at expected revision zero.</summary>
/// <param name="Version">The request format version.</param>
/// <param name="ExpectedRevision">The required absent-catalog revision, zero.</param>
/// <param name="PhysicalShardId">The explicit opaque physical shard identity.</param>
/// <param name="Incarnation">The configured replica-group incarnation.</param>
/// <param name="VoterIds">The exact ordered configured voter identities.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(PhysicalShardCatalogAliases.BootstrapRequest)]
public sealed record BootstrapPhysicalShardCatalogRequest(
    [property: Orleans.Id(PhysicalShardBootstrapRequestFieldIds.Version)] int Version,
    [property: Orleans.Id(PhysicalShardBootstrapRequestFieldIds.ExpectedRevision)] long ExpectedRevision,
    [property: Orleans.Id(PhysicalShardBootstrapRequestFieldIds.PhysicalShardId)] Guid PhysicalShardId,
    [property: Orleans.Id(PhysicalShardBootstrapRequestFieldIds.Incarnation)] Guid Incarnation,
    [property: Orleans.Id(PhysicalShardBootstrapRequestFieldIds.VoterIds)] ImmutableArray<string> VoterIds);

