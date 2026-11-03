using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRangeReader
{
    internal const int CooperativeSteps = 256;

    internal static StorageScanResult Visit(ZoneTreeStoreRuntime runtime, byte[] prefix, int maxRecords,
        StorageRecordVisitor visitor, SortedSet<ZoneTreeStagedEntry>? changes, byte[]? afterKey,
        byte[]? untilKey, StorageReadObserver? observer, CancellationToken cancellationToken, bool reverse = false)
    {
        if (!TryPrepare(runtime, prefix, maxRecords, visitor, afterKey, untilKey, observer, reverse, out var page, cancellationToken))
        {
            return default;
        }
        using var baseline = new ZoneTreeBaselineCursor(runtime, page.Lower, page.Upper, prefix, afterKey, untilKey, page.Work, reverse);
        using var staged = new ZoneTreeStagedCursor(changes, page.Lower, page.Upper, prefix, afterKey, untilKey, page.Work, reverse);
        var traversal = new ZoneTreeRangeTraversal(baseline, staged, page.Work, maxRecords, visitor, reverse);
        _ = traversal.Advance(long.MaxValue, out var result);
        return result;
    }

    internal static async ValueTask<StorageScanResult> VisitAsync(ZoneTreeStoreRuntime runtime, byte[] prefix, int maxRecords,
        StorageRecordVisitor visitor, byte[]? afterKey, CancellationToken cancellationToken)
    {
        if (!TryPrepare(runtime, prefix, maxRecords, visitor, afterKey, null, null, false, out var page, cancellationToken))
        {
            return default;
        }
        using var baseline = new ZoneTreeBaselineCursor(runtime, page.Lower, page.Upper, prefix, afterKey, null, page.Work, false);
        using var staged = new ZoneTreeStagedCursor(null, page.Lower, page.Upper, prefix, afterKey, null, page.Work, false);
        var traversal = new ZoneTreeRangeTraversal(baseline, staged, page.Work, maxRecords, visitor, false);
        StorageScanResult result;
        while (!traversal.Advance(CooperativeSteps, out result))
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return result;
    }

    private static bool TryPrepare(ZoneTreeStoreRuntime runtime, byte[] prefix, int maxRecords,
        StorageRecordVisitor visitor, byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer,
        bool reverse, out RangePage page, CancellationToken cancellationToken)
    {
        runtime.ReadCounters.RangeAttempt();
        Validate(prefix, maxRecords, visitor, runtime.Options.MaximumRangeRecords, cancellationToken);
        var lower = afterKey is not null && BinaryKeyComparer.Instance.Compare(afterKey, prefix) > 0 ? afterKey : prefix;
        var prefixSuccessor = reverse ? (StorageKeyBounds.PrefixSuccessor(prefix)).Value : null;
        if ((untilKey is not null && BinaryKeyComparer.Instance.Compare(lower, untilKey) >= 0)
            || (reverse && prefixSuccessor is not null
                && BinaryKeyComparer.Instance.Compare(lower, prefixSuccessor) >= 0))
        {
            page = default;
            return false;
        }

        var upper = reverse ? ZoneTreeRangeBounds.Minimum(untilKey, prefixSuccessor) : untilKey;
        var work = new ZoneTreeRangeWork(runtime.ReadCounters, observer, runtime.Options.MaximumRangeRecords,
            runtime.Options.MaximumRangeWorkBytes, cancellationToken);
        page = new(lower, upper, work);
        return true;
    }

    private readonly record struct RangePage(byte[] Lower, byte[]? Upper, ZoneTreeRangeWork Work);

    private static void Validate(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        int maximumRecords, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix) /* KLD0041: BCL null validation accepts the prefix byte[] reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(visitor) /* KLD0041: BCL null validation accepts the visitor StorageRecordVisitor reference through its object parameter. */;
        cancellationToken.ThrowIfCancellationRequested();
        if (maxRecords < 1 || maxRecords > maximumRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidRangeLimitMessage);
        }
    }
}
