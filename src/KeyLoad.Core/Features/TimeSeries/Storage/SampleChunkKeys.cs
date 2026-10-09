using KeyLoad.Core.Features.ClusterRouting.Contracts;
namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkKeys
{
    internal static byte[] Windows(PartitionRef partition, string set, string series)
        => KeySpace.Partition(PartitionRecordFamilies.SampleChunkWindow, partition, set, series);
    internal static byte[] Window(PartitionRef partition, string set, string series, Guid id)
        => KeySpace.Partition(PartitionRecordFamilies.SampleChunkWindow, partition, set, series, id);
    internal static byte[] Manifest(PartitionRef partition, string set, string series, Guid id, long generation)
        => KeySpace.Partition(PartitionRecordFamilies.SampleChunkManifest, partition, set, series, id, generation);
    internal static byte[] Block(PartitionRef partition, string set, string series, Guid id, long generation, int ordinal)
        => KeySpace.Partition(PartitionRecordFamilies.SampleChunkBlock, partition, set, series, id, generation, ordinal);
    internal static byte[] Correction(PartitionRef partition, string set, string series, Guid id, long sequence)
        => KeySpace.Partition(PartitionRecordFamilies.SampleChunkCorrection, partition, set, series, id, sequence);
}
