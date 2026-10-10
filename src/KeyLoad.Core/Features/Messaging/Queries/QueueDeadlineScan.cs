using System.Collections.Immutable;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal sealed class QueueDeadlineScan(DatabaseEngine database, IKeyValueView view,
    QueueDeadlineCursor cursor, DuePrefixCursor bounds, DuePageState page)
{
    private const int EqualOrder = 0;
    private const int IndexedRecordCost = 2;
    private const int MetadataRecordCost = 1;
    private const int LookaheadRecordCost = 1;
    private readonly PriorityQueue<QueueDeadlineHint, DateTimeOffset> hints = new();
    private readonly ImmutableArray<ErrorCode>.Builder rejected = ImmutableArray.CreateBuilder<ErrorCode>();
    private byte[]? last = bounds.LastKey;
    private bool atUpper;
    private long admittedBytes;

    internal bool Visit(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        page.Check();
        if (key.SequenceCompareTo(bounds.UpperKey) > EqualOrder)
        { page.ExamineRecord(); atUpper = true; return false; }
        var records = cursor.Next == QueueDeadlineCursor.MetadataIndex ? MetadataRecordCost : IndexedRecordCost;
        if (page.ExaminedRecords + records + LookaheadRecordCost > page.MaximumRecordsPerPage)
        {
            page.ExamineRecord();
            if (records + LookaheadRecordCost > page.MaximumRecordsPerPage)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.RangeBytesExceeded); }
            return false;
        }
        page.ExamineRecord();
        if (value.Length > database.Limits.MaxBatchBytes || value.Length > database.Limits.MaxBatchBytes - admittedBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, DueWorkProtocol.RangeBytesExceeded); }
        admittedBytes = checked(admittedBytes + value.Length);
        last = DueWorkRecordDecoder.CopyKey(key);
        try
        {
            if (QueueDeadlineRecordDecoder.Read(database, view, key, value, page) is { } hint)
            { hints.Enqueue(hint, hint.Mutation.ExpectedDeadline); }
        }
        catch (KeyLoadException failure) when (failure.Code is not (ErrorCode.BudgetExceeded or ErrorCode.Cancelled))
        { rejected.Add(DueWorkRecordDecoder.SafeError(failure.Code)); }
        atUpper = key.SequenceEqual(bounds.UpperKey);
        return !atUpper && page.ExaminedRecords < page.MaximumRecordsPerPage;
    }

    internal QueueDeadlinePage Complete(bool exhausted)
    {
        var jobs = ImmutableArray.CreateBuilder<QueueDeadlineHint>(hints.Count);
        while (hints.TryDequeue(out var hint, out _))
        { jobs.Add(hint); }
        var next = cursor.Advance(atUpper || exhausted ? DuePrefixCursor.Empty
            : bounds with { LastKey = last?.ToArray() });
        return new(jobs.MoveToImmutable(), rejected.ToImmutable(), next, page.ExaminedRecords, page.ExaminedBytes);
    }
}
