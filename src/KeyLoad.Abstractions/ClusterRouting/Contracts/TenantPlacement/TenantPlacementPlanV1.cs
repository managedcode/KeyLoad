using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Defines the bounded TenantPlacementPlanV1 contract.</summary>
/// <param name="Version">The original Version contract value.</param>
/// <param name="PlanId">The original PlanId contract value.</param>
/// <param name="TenantId">The original TenantId contract value.</param>
/// <param name="DatabaseId">The original DatabaseId contract value.</param>
/// <param name="PolicyRevision">The original PolicyRevision contract value.</param>
/// <param name="DirectoryRevision">The original DirectoryRevision contract value.</param>
/// <param name="CohortFingerprint">The original CohortFingerprint contract value.</param>
/// <param name="SampledAt">The original SampledAt contract value.</param>
/// <param name="ExpiresAt">The original ExpiresAt contract value.</param>
/// <param name="Moves">The original Moves contract value.</param>
/// <param name="CapacityDigests">The original CapacityDigests contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
/// <param name="OriginalPartitions">The original OriginalPartitions contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPlanV1Aliases.TenantPlacementPlanV1Alias)]
public sealed record TenantPlacementPlanV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] Guid PlanId,
    [property: global::Orleans.Id(2)] string TenantId,
    [property: global::Orleans.Id(3)] string DatabaseId,
    [property: global::Orleans.Id(4)] long PolicyRevision,
    [property: global::Orleans.Id(5)] long DirectoryRevision,
    [property: global::Orleans.Id(6)] string CohortFingerprint,
    [property: global::Orleans.Id(7)] DateTimeOffset SampledAt,
    [property: global::Orleans.Id(8)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(9)] ImmutableArray<PartitionMoveRequest> Moves,
    [property: global::Orleans.Id(10)] ImmutableArray<string> CapacityDigests,
    [property: global::Orleans.Id(11)] PartitionRef ControlPartition,
    [property: global::Orleans.Id(12)] ImmutableArray<PartitionRef> OriginalPartitions);
