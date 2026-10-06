using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal sealed class DuePageState
{
    private const int AdjacentElementOffset = 1;

    private readonly TimeProvider clock;
    private readonly TimeSpan discoveryDeadline;
    private readonly long started;
    private readonly ReadExecutionBudget budget;
    private readonly long maximumRangeBytes;
    internal int MaximumRecordsPerPage { get; }

    internal DuePageState(IOptions<DatabaseLimits> limits, DateTimeOffset wakeAt, TimeProvider clock, TimeSpan discoveryDeadline, int maximumRecordsPerPage, long maximumRangeBytes,
        CancellationToken cancellationToken)
    {
        this.clock = clock;
        started = clock.GetTimestamp();
        this.discoveryDeadline = discoveryDeadline;
        this.maximumRangeBytes = maximumRangeBytes;
        MaximumRecordsPerPage = maximumRecordsPerPage;
        WakeAt = wakeAt;
        CancellationToken = cancellationToken;
        budget = new(limits, clock, cancellationToken);
    }

    internal DateTimeOffset WakeAt { get; }
    internal CancellationToken CancellationToken { get; }
    internal int ExaminedRecords { get; private set; }
    internal long ExaminedBytes { get; private set; }

    internal void ExamineRecord() => ExaminedRecords = checked(ExaminedRecords + AdjacentElementOffset);

    internal void ObserveBytes(long count)
    {
        Check();
        if (count > maximumRangeBytes - ExaminedBytes)
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
        if (clock.GetElapsedTime(started) > discoveryDeadline)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.DeadlineExceeded);
        }
    }
}
