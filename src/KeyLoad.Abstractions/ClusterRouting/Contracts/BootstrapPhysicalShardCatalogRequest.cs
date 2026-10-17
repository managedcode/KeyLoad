using System.Collections.Immutable;

namespace KeyLoad;

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
