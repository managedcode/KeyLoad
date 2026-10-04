namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeStagedEntry(byte[] Key, byte[]? Value)
{
    internal static ZoneTreeStagedEntry High { get; } = new([], null) { IsHigh = true };
    internal bool IsHigh { get; private init; }
}

internal sealed class ZoneTreeStagedEntryComparer : IComparer<ZoneTreeStagedEntry>
{
    internal static ZoneTreeStagedEntryComparer Instance { get; } = new();

    public int Compare(ZoneTreeStagedEntry? left, ZoneTreeStagedEntry? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }
        if (left is null)
        {
            return -1;
        }
        if (right is null)
        {
            return 1;
        }
        if (left.IsHigh)
        {
            return 1;
        }
        if (right.IsHigh)
        {
            return -1;
        }

        return BinaryKeyComparer.Instance.Compare(left.Key, right.Key);
    }
}
