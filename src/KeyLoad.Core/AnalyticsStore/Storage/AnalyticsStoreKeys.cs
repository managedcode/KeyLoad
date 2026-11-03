using KeyLoad.Core.StoragePartitions.Contracts;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreKeys
{
    internal static byte[] Events(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsEvent, partition, store);
    internal static byte[] Event(PartitionRef partition, string store, string id)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsEvent, partition, store, id);
    internal static byte[] Generation(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsGeneration, partition, store);
    internal static byte[] Timestamps(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsTimestamp, partition, store);
    internal static byte[] TimestampBoundary(PartitionRef partition, string store, DateTimeOffset timestamp)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsTimestamp, partition, store, timestamp);
    internal static byte[] Timestamp(PartitionRef partition, string store, DateTimeOffset timestamp, string id)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsTimestamp, partition, store, timestamp, id);
}
