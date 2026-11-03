using KeyLoad.Storage;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleRetentionStateReader
{
    private const string RetentionSpace = "sample-retention";
    private const string CorruptState = "The time-series retention state is corrupt.";

    internal static byte[] Key(PartitionRef partition, string set, string seriesId)
        => KeySpace.Partition(RetentionSpace, partition, set, seriesId);

    internal static (bool HasValue, SampleRetentionState? Value) Read(IKeyValueView view, PartitionRef partition, string set,
        string seriesId, ReadExecutionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var key = Key(partition, set, seriesId);
        SampleRetentionState? state = null;
        var found = view.ReadValue(key, value => state = Decode(value));
        budget?.Check();
        if (!found)
        {
            return (false, default(SampleRetentionState?));
        }

        return ((SampleRetentionState?)(state)) is { } outcomeValue2 ? (true, outcomeValue2) : (false, default(SampleRetentionState?));
    }

    internal static SampleRetentionState Decode(ReadOnlySpan<byte> value)
    {
        SampleRetentionState state;
        try
        {
            state = OrleansSerialization.Deserialize<SampleRetentionState>(value);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptState);
        }

        Validate(state);
        return state;
    }

    internal static void Validate(SampleRetentionState state)
    {
        const int PurgedCountValidationBoundary = 0;

        if (state.BeforeUtcTicks < DateTimeOffset.MinValue.UtcTicks
            || state.BeforeUtcTicks > DateTimeOffset.MaxValue.UtcTicks || state.PurgedCount < PurgedCountValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptState);
        }
    }

    internal static DateTimeOffset Before(SampleRetentionState state)
        => new(state.BeforeUtcTicks, TimeSpan.Zero);
}
