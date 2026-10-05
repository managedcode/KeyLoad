using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad;

/// <summary>Contains one bounded same-owner partition query.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.Request)]
public sealed record PartitionQueryRequestV1(
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Version)] int Version,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Partitions)] ImmutableArray<PartitionRef> Partitions,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Query)] SelectQuery Query,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.Parameters)] Dictionary<string, JsonElement>? Parameters,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.AllowFullScan)] bool AllowFullScan,
    [property: Orleans.Id(PartitionQueryRequestFieldIds.AstVersion)] int AstVersion);

/// <summary>Returns one projected row with its full partition identity.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.Row)]
public sealed record PartitionQueryRowV1(
    [property: Orleans.Id(PartitionQueryRowFieldIds.Reference)] EntityRef Reference,
    [property: Orleans.Id(PartitionQueryRowFieldIds.Row)] QueryRow Row);

/// <summary>Describes the scoped read evidence for one completed leaf.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.LeafWitness)]
public sealed record PartitionQueryLeafWitnessV1(
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.CutPosition)] long CutPosition,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.PolicyEpoch)] long PolicyEpoch,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.SchemaVersion)] long SchemaVersion,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.AccessPath)] string AccessPath);

/// <summary>Returns a complete bounded result for all requested partitions.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.Page)]
public sealed record PartitionQueryPageV1(
    [property: Orleans.Id(PartitionQueryPageFieldIds.Version)] int Version,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Rows)] ImmutableArray<PartitionQueryRowV1> Rows,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Leaves)] ImmutableArray<PartitionQueryLeafWitnessV1> Leaves,
    [property: Orleans.Id(PartitionQueryPageFieldIds.Complete)] bool Complete);
