using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class QueueDeadlineDiscovery
{
    private const int SingleRecord = 1;
    private const int LookaheadRecord = 1;
    private const int Initial = 0;
    private const int IndexedReadWithLookahead = 3;

    internal static QueueDeadlinePage ReadPage(DatabaseEngine database, QueueDeadlineCursor? previous,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (now.Offset != TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.Validation, DueWorkProtocol.InvalidWakeInstant); }
        var page = new DuePageState(database.OperationLimitsOptions, now, database.EvaluationClock,
            database.DueDiscoveryDeadline, database.DueExecution.MaximumRecordsPerPage,
            database.DueExecution.MaximumRangeBytes, cancellationToken);
        page.Check();
        return database.Store.Read(view => Read(database, view,
            QueueDeadlineCursor.Match(database.Store.Identity, previous), page));
    }

    private static QueueDeadlinePage Read(DatabaseEngine database, IKeyValueView view,
        QueueDeadlineCursor cursor, DuePageState page)
    {
        page.Check();
        if (page.MaximumRecordsPerPage < IndexedReadWithLookahead
            && cursor.Next != QueueDeadlineCursor.MetadataIndex)
        {
            return new([], [], cursor.Advance(DuePrefixCursor.Empty),
                page.ExaminedRecords, page.ExaminedBytes);
        }
        var prefix = KeyCodec.Encode(cursor.Space);
        var bounds = cursor.Bounds;
        if (!bounds.HasUpperBound)
        {
            byte[]? upper = null;
            _ = view.VisitReverseRange(prefix, SingleRecord, (key, _) =>
            {
                page.Check();
                page.ExamineRecord();
                upper = DueWorkRecordDecoder.CopyKey(key);
                return false;
            }, observer: page.ObserveBytes, cancellationToken: page.CancellationToken);
            bounds = upper is null ? DuePrefixCursor.Empty : new(true, upper, null);
        }
        if (!bounds.HasUpperBound)
        { return new([], [], cursor.Advance(DuePrefixCursor.Empty), page.ExaminedRecords, page.ExaminedBytes); }
        var state = new QueueDeadlineScan(database, view, cursor, bounds, page);
        var remaining = page.MaximumRecordsPerPage - page.ExaminedRecords - LookaheadRecord;
        if (remaining <= Initial)
        { return state.Complete(false); }
        var result = view.VisitRange(prefix, remaining, state.Visit, bounds.LastKey,
            observer: page.ObserveBytes, cancellationToken: page.CancellationToken);
        if (result.HasMore && !result.StoppedByVisitor)
        { page.ExamineRecord(); }
        page.Check();
        return state.Complete(!result.HasMore && !result.StoppedByVisitor);
    }
}
