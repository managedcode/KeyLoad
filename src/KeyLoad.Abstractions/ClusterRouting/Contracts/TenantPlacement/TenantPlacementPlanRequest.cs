using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines the bounded TenantPlacementPlanRequest contract.</summary>
/// <param name="TenantId">The original TenantId contract value.</param>
/// <param name="DatabaseId">The original DatabaseId contract value.</param>
/// <param name="Partitions">The original Partitions contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPlanV1Aliases.TenantPlacementPlanRequestAlias)]
public sealed record TenantPlacementPlanRequest(
    [property: global::Orleans.Id(0)] string TenantId,
    [property: global::Orleans.Id(1)] string DatabaseId,
    [property: global::Orleans.Id(2)] ImmutableArray<PartitionRef> Partitions,
    [property: global::Orleans.Id(3)] PartitionRef ControlPartition);
