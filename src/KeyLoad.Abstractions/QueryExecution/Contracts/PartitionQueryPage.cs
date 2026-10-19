using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Returns a complete bounded result for all requested partitions.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.Page)]
public sealed record PartitionQueryPage(
    [property: Orleans.Id(PartitionQueryPageFieldIds.Version)] int Version,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Rows)] ImmutableArray<PartitionQueryRow> Rows,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Leaves)] ImmutableArray<PartitionQueryLeafWitness> Leaves,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Complete)] bool Complete);
