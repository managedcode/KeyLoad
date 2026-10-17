using System.Collections.Immutable;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

internal sealed class DuePrefixScanState
{
    private const int EqualOrder = 0;
    private const int NoRetainedBytes = 0;

    private readonly DatabaseEngine database;
    private readonly DueSweepCursor cursor;
    private readonly DueWorkKind kind;
    private readonly byte[] upper;
    private readonly DuePageState page;
    private readonly ImmutableArray<DueWorkHint>.Builder hints = ImmutableArray.CreateBuilder<DueWorkHint>();
    private readonly ImmutableArray<DueRejectedRecord>.Builder rejected = ImmutableArray.CreateBuilder<DueRejectedRecord>();
    private byte[]? lastKey;
    private long admittedBytes;
    private bool reachedUpper;
    private bool deferred;

    internal DuePrefixScanState(DatabaseEngine database, DueSweepCursor cursor, DueWorkKind kind,
        DuePrefixCursor bounds, byte[] upper, DuePageState page)
    {
        this.database = database;
        this.cursor = cursor;
        this.kind = kind;
        this.upper = upper;
        this.page = page;
        lastKey = bounds.LastKey;
    }

    internal bool Visit(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        page.Check();
        if (key.SequenceCompareTo(upper) > EqualOrder)
        {
            page.ExamineRecord();
            reachedUpper = true;
            return false;
        }
        page.ExamineRecord();
        var atUpper = key.SequenceEqual(upper);
        if (value.Length > database.Limits.MaxBatchBytes)
        {
            rejected.Add(new(kind, ErrorCode.ResourceExhausted));
            lastKey = DueWorkRecordDecoder.CopyKey(key);
            reachedUpper = atUpper;
            return false;
        }
        if (value.Length > database.Limits.MaxBatchBytes - admittedBytes)
        {
            deferred = true;
            return false;
        }

        admittedBytes += value.Length;
        lastKey = DueWorkRecordDecoder.CopyKey(key);
        AddHintOrRejection(key, value);
        reachedUpper = atUpper;
        if (page.ExaminedRecords >= page.MaximumRecordsPerPage)
        {
            deferred = !atUpper;
            return false;
        }
        return true;
    }

    internal DueWorkPage CreatePage()
    {
        var completed = reachedUpper && !deferred;
        var next = completed
            ? DueWorkCursor.Complete(cursor, kind)
            : DueWorkCursor.WithPrefix(cursor, kind, new(true, upper.ToArray(), lastKey?.ToArray()));
        return new([.. hints], [.. rejected], next, kind, completed,
            page.ExaminedRecords, page.ExaminedBytes, admittedBytes);
    }

    internal static DueWorkPage EmptyPage(DueSweepCursor cursor, DueWorkKind kind,
        DuePrefixCursor bounds, byte[] upper, DuePageState page)
    {
        var next = DueWorkCursor.WithPrefix(cursor, kind, new(true, upper.ToArray(), bounds.LastKey?.ToArray()));
        return new([], [], next, kind, false, page.ExaminedRecords, page.ExaminedBytes, NoRetainedBytes);
    }

    private void AddHintOrRejection(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        try
        {
            if (DueWorkRecordDecoder.Read(database, kind, key, value, page.WakeAt) is { } hint)
            {
                hints.Add(hint);
            }
        }
        catch (KeyLoadException exception)
        {
            rejected.Add(new(kind, DueWorkRecordDecoder.SafeError(exception.Code)));
        }
    }
}
