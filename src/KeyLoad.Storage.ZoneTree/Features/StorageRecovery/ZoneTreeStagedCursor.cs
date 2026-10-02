namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeStagedCursor(SortedSet<ZoneTreeStagedEntry>? changes,
    byte[] lower, byte[] prefix, byte[]? afterKey, byte[]? untilKey, ZoneTreeRangeWork work) : IDisposable
{
    private readonly IEnumerator<ZoneTreeStagedEntry> iterator = changes is null
        ? Enumerable.Empty<ZoneTreeStagedEntry>().GetEnumerator()
        : changes.GetViewBetween(new(lower, null), ZoneTreeStagedEntry.High).GetEnumerator();
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
                if (ZoneTreeRangeBounds.IsPast(entry.Key, prefix, untilKey))
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
}
