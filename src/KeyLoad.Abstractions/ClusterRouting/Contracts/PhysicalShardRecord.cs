using System.Collections.Immutable;

namespace KeyLoad;

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
