using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Exact globally fused requested hits with each independent authorized source cut.</summary>
/// <param name="Version">The actual distributed response version.</param>
/// <param name="Hits">Fully projected canonical documents and global fusion scores.</param>
/// <param name="Leaves">Actual independent per-partition cut and persisted-policy evidence.</param>
/// <param name="StatisticsEpoch">Opaque descriptive scoring evidence, never reusable read authority.</param>
/// <param name="Complete">Whether all required exact modality windows completed before fusion.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchContractAliases.Page)]
public sealed record DistributedSearchPageV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] ImmutableArray<RankedDocument> Hits,
    [property: global::Orleans.Id(2)] ImmutableArray<PartitionQueryLeafWitnessV1> Leaves,
    [property: global::Orleans.Id(3)] string StatisticsEpoch,
    [property: global::Orleans.Id(4)] bool Complete);
