namespace KeyLoad.Core;

internal record struct ReadExecutionBudgetNativeWork(long maximumBytes, int maximumRecords)
{
    internal const int SingleRecord = 1;
    private const int EmptyRecords = 0;
    private long byteLimit = maximumBytes;
    private int recordLimit = maximumRecords;
    private int examinedRecords;
    private bool constrained;

    internal readonly long MaximumBytes => byteLimit;
    internal readonly int MaximumRecords => recordLimit;
    internal readonly int ExaminedRecords => examinedRecords;

    internal void Constrain(long bytes, int records, long acceptedAndReservedBytes, int claimedRecords)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(records);
        if (bytes < acceptedAndReservedBytes || records < examinedRecords + claimedRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
        byteLimit = Math.Min(byteLimit, bytes);
        recordLimit = Math.Min(recordLimit, records);
        constrained = true;
    }

    internal readonly void RequireRecord(int remainingRecords)
    {
        if (constrained && remainingRecords <= EmptyRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
    }

    internal readonly void RequireObservation(int observedRecords, int remainingRecords)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedRecords);
        if (!constrained || observedRecords > remainingRecords)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded); }
    }

    internal void Record(int count)
    {
        if (constrained)
        { examinedRecords += count; }
    }
}
