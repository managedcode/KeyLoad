using KeyLoad.Core.Features.StoragePartitions.Contracts;

namespace KeyLoad.Core.Features.AnalyticsStore;

internal static class AnalyticsStoreKeys
{
    internal static byte[] Events(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsEvent, partition, store);
    internal static byte[] Event(PartitionRef partition, string store, string id)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsEvent, partition, store, id);
    internal static byte[] Generation(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsGeneration, partition, store);
}
