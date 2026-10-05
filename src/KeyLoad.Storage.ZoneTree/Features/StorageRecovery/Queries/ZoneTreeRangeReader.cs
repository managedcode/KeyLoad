using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRangeReader
{
    internal static StorageScanResult Visit(ZoneTreeStoreRuntime runtime, byte[] prefix, int maxRecords,
        StorageRecordVisitor visitor, SortedSet<ZoneTreeStagedEntry>? changes, byte[]? afterKey,
        byte[]? untilKey, StorageReadObserver? observer, CancellationToken cancellationToken, bool reverse = false)
    {
        runtime.ReadCounters.RangeAttempt();
        Validate(prefix, maxRecords, visitor, cancellationToken);
        var lower = afterKey is not null && BinaryKeyComparer.Instance.Compare(afterKey, prefix) > 0 ? afterKey : prefix;
        var prefixSuccessor = reverse ? ZoneTreeRangeBounds.PrefixSuccessor(prefix) : null;
        if ((untilKey is not null && BinaryKeyComparer.Instance.Compare(lower, untilKey) >= 0)
            || (reverse && prefixSuccessor is not null
                && BinaryKeyComparer.Instance.Compare(lower, prefixSuccessor) >= 0))
        {
            return default;
        }

        var upper = reverse ? ZoneTreeRangeBounds.Minimum(untilKey, prefixSuccessor) : untilKey;
        var work = new ZoneTreeRangeWork(runtime.ReadCounters, observer, cancellationToken);
        using var baseline = new ZoneTreeBaselineCursor(runtime.Tree, lower, upper, prefix, afterKey, untilKey, work, reverse);
        using var staged = new ZoneTreeStagedCursor(changes, lower, upper, prefix, afterKey, untilKey, work, reverse);
        return VisitMerged(baseline, staged, work, maxRecords, visitor, reverse);
    }

    private static StorageScanResult VisitMerged(ZoneTreeBaselineCursor baseline, ZoneTreeStagedCursor staged,
        ZoneTreeRangeWork work, int maxRecords, StorageRecordVisitor visitor, bool reverse)
    {
        var hasBaseline = baseline.MoveNext();
        var hasStaged = staged.MoveNext();
        var delivered = 0;
        while (hasBaseline || hasStaged)
        {
            work.Check();
            var comparison = hasBaseline && hasStaged
                ? baseline.Key.Span.SequenceCompareTo(staged.Key) * (reverse ? -1 : 1)
                : hasBaseline ? -1 : 1;
            if (ProcessCurrent(baseline, staged, work, comparison, maxRecords, visitor, ref delivered) is { } result)
            {
                return result;
            }

            if (hasBaseline && comparison <= 0)
            {
                hasBaseline = baseline.MoveNext();
            }
            if (hasStaged && comparison >= 0)
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
        var useStaged = comparison >= 0;
        var key = useStaged ? staged.Key.AsSpan() : baseline.Key.Span;
        var value = useStaged ? staged.Value : null;
        if (useStaged)
        {
            work.ChargeStaged((long)staged.Key.Length + (value?.Length ?? 0), value is null);
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        cancellationToken.ThrowIfCancellationRequested();
        if (maxRecords is < 1 or > MaximumRangeRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidRangeLimitMessage);
        }
    }
}
