namespace KeyLoad;

/// <summary>Identifies a projection consumer within a partition.</summary>
/// <param name="Partition">The partition containing the outbox.</param>
/// <param name="Name">The projection consumer name.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ProjectionConsumerRef)]
public sealed record ProjectionConsumerRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Name);
