namespace KeyLoad;

/// <summary>Identifies one blob within its complete atomic and resource scope.</summary>
/// <param name="Partition">The atomic partition, including its partition key.</param>
/// <param name="Resource">The configured binary resource.</param>
/// <param name="Id">The object identity within that scope.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobRef)]
public sealed record BlobRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Resource, [property: Orleans.Id(2)] string Id);
