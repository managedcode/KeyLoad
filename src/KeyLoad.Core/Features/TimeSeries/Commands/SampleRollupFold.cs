using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRollupFold
{
    private const long FirstSequence = 1;
    internal static SampleAggregate Read(IKeyValueView view, PartitionRef partition, RefreshSampleRollup request,
        long sequence, SampleRollupReadCharge charge)
    {
        var accumulator = new SampleAggregateAccumulator();
        var scan = view.VisitRange(SampleReadKeys.Prefix(partition, request.SeriesSet, request.SeriesId),
            request.MaxSamples, (key, value) =>
            {
                var record = NativeSerialization.Deserialize<SampleRecord>(value);
                Validate(key, record, partition, request, sequence);
                accumulator.Add(record);
                return true;
            }, SampleReadKeys.FromInclusive(partition, request.SeriesSet, request.SeriesId, request.From),
            SampleReadKeys.UntilExclusive(partition, request.SeriesSet, request.SeriesId, request.UntilExclusive),
            observer: charge.Charge);
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SampleAggregateReader.SampleBudgetExceeded); }
        return accumulator.Complete();
    }
    private static void Validate(ReadOnlySpan<byte> key, SampleRecord record, PartitionRef partition,
        RefreshSampleRollup request, long sequence)
    {
        if (record is null || record.Sample is null || record.SeriesId != request.SeriesId
            || record.Sequence < FirstSequence || record.Sequence > sequence || !double.IsFinite(record.Sample.Value)
            || record.Sample.Timestamp.UtcTicks < request.From.UtcTicks
            || record.Sample.Timestamp.UtcTicks >= request.UntilExclusive.UtcTicks)
        { throw Errors.Fail(ErrorCode.Corruption, SampleRollupProtocol.Corrupt); }
        var canonical = KeySpace.Partition(KeyLoad.Core.Features.ClusterRouting.Contracts.PartitionRecordFamilies.Sample, partition,
            request.SeriesSet, request.SeriesId, record.Sample.Timestamp, record.Sequence);
        if (!key.SequenceEqual(canonical))
        { throw Errors.Fail(ErrorCode.Corruption, SampleRollupProtocol.Corrupt); }
    }
}
