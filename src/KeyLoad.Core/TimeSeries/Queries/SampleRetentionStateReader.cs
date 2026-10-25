using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleRetentionStateReader
{
    internal const int CurrentFormatVersion = 1;
    private const string RetentionSpace = "sample-retention";
    private const string UnsupportedFormat = "The time-series retention format is unsupported.";
    private const string CorruptState = "The time-series retention state is corrupt.";

    internal static byte[] Key(PartitionRef partition, string set, string seriesId)
        => KeySpace.Partition(RetentionSpace, partition, set, seriesId);

    internal static global::ManagedCode.Communication.Result<SampleRetentionState?> Read(IKeyValueView view, PartitionRef partition, string set,
        string seriesId, ReadExecutionBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var key = Key(partition, set, seriesId);
        SampleRetentionState? state = null;
        StorageReadObserver? observer = budget is not null && view is not BudgetedReadView
            ? budget.ChargeBytes : null;
        var found = view.ReadValue(key, value => state = Decode(value), observer);
        budget?.Check();
        if (!found)
        {
            return global::ManagedCode.Communication.Result<SampleRetentionState?>.FailNotFound(nameof(Read));
        }

        return ((SampleRetentionState?)(state)) is { } outcomeValue2 ? global::ManagedCode.Communication.Result<SampleRetentionState?>.Succeed(outcomeValue2) : global::ManagedCode.Communication.Result<SampleRetentionState?>.FailNotFound(nameof(Read));
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

        if (state.FormatVersion != CurrentFormatVersion)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, UnsupportedFormat);
        }

        if (state.BeforeUtcTicks < DateTimeOffset.MinValue.UtcTicks
            || state.BeforeUtcTicks > DateTimeOffset.MaxValue.UtcTicks || state.PurgedCount < PurgedCountValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptState);
        }
    }

    internal static DateTimeOffset Before(SampleRetentionState state)
        => new(state.BeforeUtcTicks, TimeSpan.Zero);
}
