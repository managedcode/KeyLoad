namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeStagedCursor(SortedSet<ZoneTreeStagedEntry>? changes,
    byte[] lower, byte[]? upper, byte[] prefix, byte[]? afterKey, byte[]? untilKey,
    ZoneTreeRangeWork work, bool reverse) : IDisposable
{
    private readonly IEnumerator<ZoneTreeStagedEntry> iterator = changes is null
        ? Enumerable.Empty<ZoneTreeStagedEntry>().GetEnumerator()
        : GetRange(changes, lower, upper, reverse).GetEnumerator();
    private readonly byte[] prefix = prefix;
    private readonly byte[]? afterKey = afterKey;
    private readonly byte[]? untilKey = untilKey;
    private readonly ZoneTreeRangeWork work = work;

    internal byte[] Key { get; private set; } = [];
    internal byte[]? Value { get; private set; }

    internal bool MoveNext()
    {
        while (true)
        {
            work.Check();
            if (!iterator.MoveNext())
            {
                return false;
            }

            var entry = iterator.Current;
            if (!ZoneTreeRangeBounds.Contains(entry.Key, prefix, afterKey, untilKey))
            {
                if (ZoneTreeRangeBounds.IsPast(entry.Key, prefix, afterKey, untilKey, reverse))
                {
                    return false;
                }
                continue;
            }

            Key = entry.Key;
            Value = entry.Value;
            return true;
        }
    }

    public void Dispose() => iterator.Dispose();

    private static IEnumerable<ZoneTreeStagedEntry> GetRange(SortedSet<ZoneTreeStagedEntry> changes,
        byte[] lower, byte[]? upper, bool reverse)
    {
        var upperEntry = upper is null ? ZoneTreeStagedEntry.High : new(upper, null);
        var view = changes.GetViewBetween(new(lower, null), upperEntry);
        return reverse ? view.Reverse() : view;
    }
}
