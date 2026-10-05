using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRangeReader
{
    private const int EqualKeys = 0;
    private const int NoDeliveredRecords = 0;
    private const int LowerKey = -1;
    private const int HigherKey = 1;
    private const int NoValueBytes = 0;

    private const int MinimumRequestedRecords = 1;
    internal static StorageScanResult Visit(ZoneTreeStoreRuntime runtime, byte[] prefix, int maxRecords,
        StorageRecordVisitor visitor, SortedSet<ZoneTreeStagedEntry>? changes, byte[]? afterKey,
        byte[]? untilKey, StorageReadObserver? observer, CancellationToken cancellationToken, bool reverse = false)
    {
        runtime.ReadCounters.RangeAttempt();
        Validate(prefix, maxRecords, visitor, cancellationToken, runtime.Options.MaximumRangeRecords);
        var lower = afterKey is not null && BinaryKeyComparer.Instance.Compare(afterKey, prefix) > EqualKeys ? afterKey : prefix;
        var prefixSuccessor = reverse ? ZoneTreeRangeBounds.PrefixSuccessor(prefix) : null;
        if ((untilKey is not null && BinaryKeyComparer.Instance.Compare(lower, untilKey) >= EqualKeys)
            || (reverse && prefixSuccessor is not null
                && BinaryKeyComparer.Instance.Compare(lower, prefixSuccessor) >= EqualKeys))
        {
            return default;
        }

        var upper = reverse ? ZoneTreeRangeBounds.Minimum(untilKey, prefixSuccessor) : untilKey;
        var work = new ZoneTreeRangeWork(runtime.ReadCounters, observer, cancellationToken,
            runtime.Options.MaximumRangeRecords, runtime.Options.MaximumRangeWorkBytes);
        using var baseline = new ZoneTreeBaselineCursor(runtime.Tree, lower, upper, prefix, afterKey, untilKey, work, reverse);
        using var staged = new ZoneTreeStagedCursor(changes, lower, upper, prefix, afterKey, untilKey, work, reverse);
        return VisitMerged(baseline, staged, work, maxRecords, visitor, reverse);
    }

    private static StorageScanResult VisitMerged(ZoneTreeBaselineCursor baseline, ZoneTreeStagedCursor staged,
        ZoneTreeRangeWork work, int maxRecords, StorageRecordVisitor visitor, bool reverse)
    {
        var hasBaseline = baseline.MoveNext();
        var hasStaged = staged.MoveNext();
        var delivered = NoDeliveredRecords;
        while (hasBaseline || hasStaged)
        {
            work.Check();
            var comparison = hasBaseline && hasStaged
                ? baseline.Key.Span.SequenceCompareTo(staged.Key) * (reverse ? LowerKey : HigherKey)
                : hasBaseline ? LowerKey : HigherKey;
            if (ProcessCurrent(baseline, staged, work, comparison, maxRecords, visitor, ref delivered) is { } result)
            {
                return result;
            }

            if (hasBaseline && comparison <= EqualKeys)
            {
                hasBaseline = baseline.MoveNext();
            }
            if (hasStaged && comparison >= EqualKeys)
            {
                hasStaged = staged.MoveNext();
            }
        }

        return new(delivered, false, false, work.ReadBytes);
    }

    private static StorageScanResult? ProcessCurrent(ZoneTreeBaselineCursor baseline,
        ZoneTreeStagedCursor staged, ZoneTreeRangeWork work, int comparison, int maxRecords,
        StorageRecordVisitor visitor, ref int delivered)
    {
        var useStaged = comparison >= EqualKeys;
        var key = useStaged ? staged.Key.AsSpan() : baseline.Key.Span;
        var value = useStaged ? staged.Value : null;
        if (useStaged)
        {
            work.ChargeStaged((long)staged.Key.Length + (value?.Length ?? NoValueBytes), value is null);
        }

        var live = !useStaged || value is not null;
        if (live)
        {
            if (delivered == maxRecords)
            {
                work.CountLookahead();
                return new(delivered, true, false, work.ReadBytes);
            }

            var keepGoing = useStaged ? visitor(key, value!) : visitor(key, baseline.Value.Span);
            delivered++;
            if (!keepGoing)
            {
                return new(delivered, false, true, work.ReadBytes);
            }
        }
        return null;
    }

    private static void Validate(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        CancellationToken cancellationToken, int maximumRecords)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxRecords < MinimumRequestedRecords || maxRecords > maximumRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidRangeLimitMessage);
        }
    }
}
