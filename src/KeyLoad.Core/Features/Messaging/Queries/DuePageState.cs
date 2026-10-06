using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal sealed class DuePageState
{
    private const int AdjacentElementOffset = 1;

    private readonly TimeSpan discoveryDeadline;
    private readonly long started;
    private readonly ReadExecutionBudget budget;
    private readonly long maximumRangeBytes;
    internal int MaximumRecordsPerPage { get; }

    internal DuePageState(IOptions<DatabaseLimits> limits, DateTimeOffset wakeAt, long started, TimeSpan discoveryDeadline, int maximumRecordsPerPage, long maximumRangeBytes,
        CancellationToken cancellationToken)
    {
        this.started = started;
        this.discoveryDeadline = discoveryDeadline;
        this.maximumRangeBytes = maximumRangeBytes;
        MaximumRecordsPerPage = maximumRecordsPerPage;
        WakeAt = wakeAt;
        CancellationToken = cancellationToken;
        budget = new(limits, cancellationToken: cancellationToken);
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
        if (Stopwatch.GetElapsedTime(started) > discoveryDeadline)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.DeadlineExceeded);
        }
    }
}
