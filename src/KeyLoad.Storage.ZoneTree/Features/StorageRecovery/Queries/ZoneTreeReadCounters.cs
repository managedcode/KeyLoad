namespace KeyLoad.Storage.ZoneTree;

/// <summary>Constant-cardinality, process-local logical read counters for one store opening.</summary>
internal sealed class ZoneTreeReadCounters
{
    private const int SingleReadEvent = 1;
    private const int NoCounterIncrement = 0;

    private readonly Guid sessionId = Guid.NewGuid();
    private long ownedPointLookups;
    private long borrowedPointLookups;
    private long pointExaminedBytes;
    private long rangeVisitAttempts;
    private long rangeBaselineEntries;
    private long rangeStagedEntries;
    private long rangeStagedTombstones;
    private long rangeLimitLookaheads;
    private long rangeExaminedBytes;

    internal Guid SessionId => sessionId;

    internal void Point(bool owned, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        SaturatingIncrement(ref (owned ? ref ownedPointLookups : ref borrowedPointLookups));
        SaturatingAdd(ref pointExaminedBytes, bytes);
    }

    internal void RangeAttempt() => SaturatingIncrement(ref rangeVisitAttempts);

    internal void RangeEntry(long bytes, bool baseline, bool tombstone = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        SaturatingIncrement(ref (baseline ? ref rangeBaselineEntries : ref rangeStagedEntries));
        if (!baseline && tombstone)
        {
            SaturatingIncrement(ref rangeStagedTombstones);
        }
        SaturatingAdd(ref rangeExaminedBytes, bytes);
    }

    internal void Lookahead() => SaturatingIncrement(ref rangeLimitLookaheads);

    internal ZoneTreeReadSnapshot Snapshot() => new(
        sessionId,
        Interlocked.Read(ref ownedPointLookups),
        Interlocked.Read(ref borrowedPointLookups),
        Interlocked.Read(ref pointExaminedBytes),
        Interlocked.Read(ref rangeVisitAttempts),
        Interlocked.Read(ref rangeBaselineEntries),
        Interlocked.Read(ref rangeStagedEntries),
        Interlocked.Read(ref rangeStagedTombstones),
        Interlocked.Read(ref rangeLimitLookaheads),
        Interlocked.Read(ref rangeExaminedBytes));

    private static void SaturatingIncrement(ref long counter) => SaturatingAdd(ref counter, SingleReadEvent);

    private static void SaturatingAdd(ref long counter, long amount)
    {
        if (amount == NoCounterIncrement)
        {
            return;
        }

        while (true)
        {
            var observed = Interlocked.Read(ref counter);
            if (observed == long.MaxValue)
            {
                return;
            }
            var next = amount >= long.MaxValue - observed ? long.MaxValue : observed + amount;
            if (Interlocked.CompareExchange(ref counter, next, observed) == observed)
            {
                return;
            }
        }
    }
}
