using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleRollupRecords
{
    private const long EmptySequence = 0;
    internal static SampleRollupState? Read(IKeyValueView view, PartitionRef partition, string set, string series,
        DateTimeOffset from, DateTimeOffset until, StorageReadObserver? observer = null)
    {
        SampleRollupState? state = null;
        view.ReadValue(SampleRollupKeys.Bucket(partition, set, series, from, until), value =>
        {
            state = NativeSerialization.Deserialize<SampleRollupState>(value)
                ?? throw Errors.Fail(ErrorCode.Corruption, SampleRollupProtocol.Corrupt);
            SampleRollupValidation.State(state, from, until);
        }, observer);
        return state;
    }

    internal static (long Sequence, long? Floor) Watermark(IKeyValueView view, PartitionRef partition,
        string set, string series, StorageReadObserver? observer = null)
    {
        var sequence = EmptySequence;
        view.ReadValue(SampleRollupKeys.Sequence(partition, set, series),
            value => sequence = NativeSerialization.Deserialize<long>(value), observer);
        long? floor = null;
        view.ReadValue(SampleRetentionStateReader.Key(partition, set, series),
            value => floor = SampleRetentionStateReader.Decode(value).BeforeUtcTicks, observer);
        if (sequence < EmptySequence)
        { throw Errors.Fail(ErrorCode.Corruption, SampleRollupProtocol.Corrupt); }
        return (sequence, floor);
    }
}
