namespace KeyLoad;

/// <summary>Requests bounded deletion of outbox entries through a sequence.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Partition">The partition whose outbox is purged.</param>
/// <param name="ThroughSequence">The inclusive upper sequence bound.</param>
/// <param name="Limit">The maximum number of entries to purge.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.PurgeOutboxRequest)]
public sealed record PurgeOutboxRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] PartitionRef Partition, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] int Limit = PurgeOutboxRequest.DefaultLimit)
{
    private const int DefaultLimit = 1_000;
}
