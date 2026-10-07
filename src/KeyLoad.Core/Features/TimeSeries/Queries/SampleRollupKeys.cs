using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRollupKeys
{
    internal static byte[] Prefix(PartitionRef partition, string set, string series)
        => KeySpace.Partition(PartitionRecordFamilies.SampleRollup, partition, set, series);
    internal static byte[] Bucket(PartitionRef partition, string set, string series, DateTimeOffset from,
        DateTimeOffset until) => KeySpace.Partition(PartitionRecordFamilies.SampleRollup, partition, set, series,
            from.ToUniversalTime(), until.ToUniversalTime());
    internal static byte[] Sequence(PartitionRef partition, string set, string series)
        => KeySpace.Partition(PartitionRecordFamilies.SampleSequence, partition, set, series);
}
