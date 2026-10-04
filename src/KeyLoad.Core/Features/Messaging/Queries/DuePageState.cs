using System.Diagnostics;

namespace KeyLoad.Core;

internal sealed class DuePageState
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(100);
    private readonly long started;
    private readonly ReadExecutionBudget budget;

    internal DuePageState(DatabaseLimits limits, DateTimeOffset wakeAt, long started,
        CancellationToken cancellationToken)
    {
        this.started = started;
        WakeAt = wakeAt;
        CancellationToken = cancellationToken;
        budget = new(limits, cancellationToken: cancellationToken);
    }

    internal DateTimeOffset WakeAt { get; }
    internal CancellationToken CancellationToken { get; }
    internal int ExaminedRecords { get; private set; }
    internal long ExaminedBytes { get; private set; }

    internal void ExamineRecord() => ExaminedRecords = checked(ExaminedRecords + 1);

    internal void ObserveBytes(long count)
    {
        Check();
        if (count > DueWorkProtocol.NativeRangeByteCeiling - ExaminedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.RangeBytesExceeded);
        }
        budget.ChargeBytes(count);
        ExaminedBytes = checked(ExaminedBytes + count);
    }

    internal void Check()
    {
        CancellationToken.ThrowIfCancellationRequested();
        budget.Check();
        if (Stopwatch.GetElapsedTime(started) > Deadline)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.DeadlineExceeded);
        }
    }
}
