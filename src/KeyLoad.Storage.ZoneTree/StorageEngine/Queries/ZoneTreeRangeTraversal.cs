namespace KeyLoad.Storage.ZoneTree;

/// <summary>Advances one logical page without replacing its native cursors or work meter.</summary>
internal struct ZoneTreeRangeTraversal(ZoneTreeBaselineCursor baseline, ZoneTreeStagedCursor staged,
    ZoneTreeRangeWork work, int maxRecords, StorageRecordVisitor visitor, bool reverse)
{
    private const int LowerKey = -1;
    private bool started;
    private bool hasBaseline;
    private bool hasStaged;
    private int delivered;

    internal bool Advance(long maximumSteps, out StorageScanResult result)
    {
        if (!started)
        {
            hasBaseline = baseline.MoveNext();
            hasStaged = staged.MoveNext();
            started = true;
        }
        for (var remaining = maximumSteps; (hasBaseline || hasStaged) && remaining > 0; remaining--)
        {
            work.Check();
            var comparison = hasBaseline && hasStaged
                ? baseline.Key.Span.SequenceCompareTo(staged.Key) * (reverse ? LowerKey : 1)
                : hasBaseline ? LowerKey : 1;
            if (ProcessCurrent(comparison, out result))
            {
                return true;
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
        result = new(delivered, false, false, work.ReadBytes);
        return !hasBaseline && !hasStaged;
    }

    private bool ProcessCurrent(int comparison, out StorageScanResult result)
    {
        var useStaged = comparison >= 0;
        var key = useStaged ? staged.Key.AsSpan() : baseline.Key.Span;
        var value = useStaged ? staged.Value : null;
        if (useStaged)
        {
            work.ChargeStaged((long)staged.Key.Length + (value?.Length ?? 0), value is null);
        }
        if (useStaged && value is null)
        {
            result = default;
            return false;
        }
        if (delivered == maxRecords)
        {
            work.CountLookahead();
            result = new(delivered, true, false, work.ReadBytes);
            return true;
        }
        var keepGoing = useStaged ? visitor(key, value!) : visitor(key, baseline.Value.Span);
        delivered++;
        work.Check();
        result = keepGoing ? default : new(delivered, false, true, work.ReadBytes);
        return !keepGoing;
    }
}
