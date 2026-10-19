namespace KeyLoad;

/// <summary>Returns one projected row with its full partition identity.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(PartitionQueryContractAliases.Row)]
public sealed record PartitionQueryRow(
    [property: Orleans.Id(PartitionQueryRowFieldIds.Reference)] EntityRef Reference,
    [property: Orleans.Id(PartitionQueryRowFieldIds.Row)] QueryRow Row);
