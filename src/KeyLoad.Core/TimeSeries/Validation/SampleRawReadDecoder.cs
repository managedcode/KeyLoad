namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRawReadDecoder
{
    private const long FirstSequence = 1;
    private const string CorruptSample = "A stored time-series sample does not match its canonical identity.";

    internal static SampleRecord Read(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        PartitionRef partition, string set, string series)
    {
        var record = OrleansSerialization.Deserialize<SampleRecord>(value);
        if (record is null || record.Sample is null || record.SeriesId != series
            || record.Sequence < FirstSequence || !double.IsFinite(record.Sample.Value))
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSample);
        }

        var canonical = KeySpace.Partition(KeyLoad.Core.Features.StoragePartitions.Contracts.PartitionRecordFamilies.Sample,
            partition, set, series, record.Sample.Timestamp, record.Sequence);
        if (!key.SequenceEqual(canonical))
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptSample);
        }
        return record;
    }
}
