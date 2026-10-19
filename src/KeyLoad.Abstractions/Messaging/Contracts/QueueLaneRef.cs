namespace KeyLoad;

/// <summary>Identifies a work queue in an atomic partition.</summary>
/// <param name="Partition">Identifies the atomic partition containing the resource.</param>
/// <param name="Queue">Identifies the work queue.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.QueueLaneRef)]
public sealed record QueueLaneRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Queue);
