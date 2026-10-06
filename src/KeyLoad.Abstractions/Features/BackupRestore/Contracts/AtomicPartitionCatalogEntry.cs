namespace KeyLoad;

/// <summary>Records the first atomic commit that observed one logical partition.</summary>
/// <param name="Version">The roster entry format version.</param>
/// <param name="Partition">The complete four-field logical partition identity.</param>
/// <param name="FirstSeenStorePosition">The local ordered store position, or zero for a replicated entry.</param>
/// <param name="FirstSeenAppliedIndex">The first replicated applied index, or zero for local-only commits.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionCatalogEntryAliases.Entry)]
public sealed record AtomicPartitionCatalogEntryV1(
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.FirstSeenStorePosition)] long FirstSeenStorePosition,
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.FirstSeenAppliedIndex)] long FirstSeenAppliedIndex);
