using System.Collections.Immutable;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal sealed class QueueDeadlineScan(IKeyValueView view,
    QueueDeadlineCursor cursor, DuePrefixCursor bounds, DuePageState page)
{
    private const int IndexedRecordCost = 2;
    private readonly PriorityQueue<QueueDeadlineHint, DateTimeOffset> hints = new();
    private readonly ImmutableArray<ErrorCode>.Builder rejected = ImmutableArray.CreateBuilder<ErrorCode>();
    private byte[]? last = bounds.LastKey?.ToArray();
    private readonly ImmutableArray<byte> upper = bounds.UpperKey
        ?? throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidCursor);
    private bool atUpper;

    internal bool Visit(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        page.Check();
        if (key.SequenceCompareTo(upper.AsSpan()) > 0)
        {
            page.ExamineRecord();
            atUpper = true;
            return false;
        }
        var records = cursor.Next == QueueDeadlineCursor.MetadataIndex ? 1 : IndexedRecordCost;
        if (page.ExaminedRecords + records + 1 > page.MaximumRecordsPerPage)
        {
            page.ExamineRecord();
            if (records + 1 > page.MaximumRecordsPerPage)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, DueWorkProtocol.RangeBytesExceeded);
            }
            return false;
        }
        page.ExamineRecord();
        last = DueWorkRecordDecoder.CopyKey(key);
        try
        {
            if (QueueDeadlineRecordDecoder.Read(view, key, value, page) is { } hint)
            {
                hints.Enqueue(hint, hint.Mutation.ExpectedDeadline);
            }
        }
        catch (KeyLoadException failure) when (failure.Code is not (ErrorCode.BudgetExceeded or ErrorCode.Cancelled))
        {
            rejected.Add(DueWorkRecordDecoder.SafeError(failure.Code));
        }
        atUpper = key.SequenceEqual(upper.AsSpan());
        return !atUpper && page.ExaminedRecords < page.MaximumRecordsPerPage;
    }

    internal QueueDeadlinePage Complete(bool exhausted)
    {
        var jobs = ImmutableArray.CreateBuilder<QueueDeadlineHint>(hints.Count);
        while (hints.TryDequeue(out var hint, out _))
        {
            jobs.Add(hint);
        }
        var next = cursor.Advance(atUpper || exhausted ? DuePrefixCursor.Empty
            : bounds with
            {
                LastKey = last is null ? null : ImmutableArray.CreateRange(last)
            });
        return new(jobs.MoveToImmutable(), rejected.ToImmutable(), next, page.ExaminedRecords, page.ExaminedBytes);
    }
}
