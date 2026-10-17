using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns a complete bounded result for all requested partitions.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.Page)]
public sealed record PartitionQueryPageV1(
    [property: Orleans.Id(PartitionQueryPageFieldIds.Version)] int Version,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Rows)] ImmutableArray<PartitionQueryRowV1> Rows,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Leaves)] ImmutableArray<PartitionQueryLeafWitnessV1> Leaves,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Complete)] bool Complete);
