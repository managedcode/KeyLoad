namespace KeyLoad;

/// <summary>Identifies the partition whose outbox status is requested.</summary>
/// <param name="Partition">The partition to inspect.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.GetOutboxStatusRequest)]
public sealed record GetOutboxStatusRequest([property: Orleans.Id(0)] PartitionRef Partition);
