using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreKeys
{
    internal static byte[] Events(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsEvent, partition, store);
    internal static byte[] Event(PartitionRef partition, string store, string id)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsEvent, partition, store, id);
    internal static byte[] Event(ReadOnlySpan<byte> prefix, string id)
        => KeyCodec.AppendComponent(prefix, id);
    internal static byte[] Generation(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsGeneration, partition, store);
    internal static byte[] Timestamps(PartitionRef partition, string store)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsTimestamp, partition, store);
    internal static byte[] TimestampBoundary(PartitionRef partition, string store, DateTimeOffset timestamp)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsTimestamp, partition, store, timestamp);
    internal static byte[] Timestamp(PartitionRef partition, string store, DateTimeOffset timestamp, string id)
        => KeySpace.Partition(PartitionRecordFamilies.AnalyticsTimestamp, partition, store, timestamp, id);

    internal static bool MatchesTimestamp(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, DateTimeOffset timestamp, string id)
        => key.StartsWith(prefix) && KeyCodec.MatchesComponents(key[prefix.Length..], timestamp, id);
}
