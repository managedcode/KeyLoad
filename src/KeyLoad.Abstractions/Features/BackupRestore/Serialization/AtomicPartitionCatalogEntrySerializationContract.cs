namespace KeyLoad;

internal static class AtomicPartitionCatalogEntryAliases
{
    internal const string Entry = "keyload.backup.atomic-partition-catalog-entry.v1";
}

internal static class AtomicPartitionCatalogEntryFieldIds
{
    internal const uint Version = 0;
    internal const uint Partition = 1;
    internal const uint FirstSeenStorePosition = 2;
    internal const uint FirstSeenAppliedIndex = 3;
}
