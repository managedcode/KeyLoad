using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class DuePrefixScanner
{
    private const int SingleElementCount = 1;
    private const int AdjacentElementOffset = 1;
    private const int EmptyElementCount = 0;
    private const int NoRetainedBytes = 0;

    internal static DueWorkPage Read(DatabaseEngine database, IKeyValueView view, DueSweepCursor cursor,
        DuePageState state, byte[] prefix)
    {
        var kind = cursor.NextPrefix;
        var bounds = DueWorkCursor.GetPrefix(cursor, kind);
        if (!bounds.HasUpperBound)
        {
            bounds = CaptureUpper(view, prefix, state);
        }
        if (!bounds.HasUpperBound)
        {
            return CompletePage(cursor, kind, state);
        }
        var upper = bounds.UpperKey ?? throw Errors.Fail(ErrorCode.Corruption, DueWorkProtocol.InvalidCursor);
        return ScanBoundedPrefix(database, view, cursor, kind, bounds, upper, prefix, state);
    }

    private static DuePrefixCursor CaptureUpper(IKeyValueView view, byte[] prefix, DuePageState state)
    {
        byte[]? upper = null;
        _ = view.VisitReverseRange(prefix, SingleElementCount, (key, _) =>
        {
            state.Check();
            state.ExamineRecord();
            upper = DueWorkRecordDecoder.CopyKey(key);
            return false;
        }, observer: state.ObserveBytes, cancellationToken: state.CancellationToken);
        return upper is null ? DuePrefixCursor.Empty : new(true, upper, null);
    }

    private static DueWorkPage ScanBoundedPrefix(DatabaseEngine database, IKeyValueView view,
        DueSweepCursor cursor, DueWorkKind kind, DuePrefixCursor bounds, byte[] upper, byte[] prefix,
        DuePageState state)
    {
        var remaining = state.MaximumRecordsPerPage - state.ExaminedRecords - AdjacentElementOffset;
        if (remaining <= EmptyElementCount)
        {
            return DuePrefixScanState.EmptyPage(cursor, kind, bounds, upper, state);
        }
        var scanState = new DuePrefixScanState(database, cursor, kind, bounds, upper, state);
        var scan = view.VisitRange(prefix, remaining, scanState.Visit, bounds.LastKey,
            observer: state.ObserveBytes, cancellationToken: state.CancellationToken);
        if (scan.Records == EmptyElementCount && !scan.HasMore && !scan.StoppedByVisitor)
        {
            return CompletePage(cursor, kind, state);
        }
        if (scan.HasMore)
        {
            state.ExamineRecord();
        }
        return scanState.CreatePage();
    }

    private static DueWorkPage CompletePage(DueSweepCursor cursor, DueWorkKind kind, DuePageState state)
    {
        var next = DueWorkCursor.Complete(cursor, kind);
        return new([], [], next, kind, true, state.ExaminedRecords, state.ExaminedBytes, NoRetainedBytes);
    }
}
