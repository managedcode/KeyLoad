namespace KeyLoad;

/// <summary>Records the first atomic commit that observed one logical partition.</summary>
/// <param name="Version">The roster entry format version.</param>
/// <param name="Partition">The complete four-field logical partition identity.</param>
/// <param name="FirstSeenStorePosition">The local ordered store position, or zero when first observed by a canonical command.</param>
/// <param name="FirstSeenAppliedIndex">The first canonical command sequence, or zero for silo-local derived work.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionCatalogEntryAliases.Entry)]
public sealed record AtomicPartitionCatalogEntry(
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.FirstSeenStorePosition)] long FirstSeenStorePosition,
    [property: Orleans.Id(AtomicPartitionCatalogEntryFieldIds.FirstSeenAppliedIndex)] long FirstSeenAppliedIndex);
