namespace KeyLoad;

/// <summary>Describes the scoped read evidence for one completed leaf.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.LeafWitness)]
public sealed record PartitionQueryLeafWitness(
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.CutPosition)] long CutPosition,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.PolicyEpoch)] long PolicyEpoch,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.SchemaVersion)] long SchemaVersion,
    [property: Orleans.Id(PartitionQueryLeafWitnessFieldIds.AccessPath)] string AccessPath);
