namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeStagedEntry(byte[] Key, byte[]? Value)
{
    internal static ZoneTreeStagedEntry High { get; } = new([], null) { IsHigh = true };
    internal bool IsHigh { get; private init; }
}

internal sealed class ZoneTreeStagedEntryComparer : IComparer<ZoneTreeStagedEntry>
{
    private const int EqualKeys = 0;
    private const int LowerKey = -1;
    private const int HigherKey = 1;

    internal static ZoneTreeStagedEntryComparer Instance { get; } = new();

    public int Compare(ZoneTreeStagedEntry? left, ZoneTreeStagedEntry? right)
    {
        if (ReferenceEquals(left, right))
        {
            return EqualKeys;
        }
        if (left is null)
        {
            return LowerKey;
        }
        if (right is null)
        {
            return HigherKey;
        }
        if (left.IsHigh)
        {
            return HigherKey;
        }
        if (right.IsHigh)
        {
            return LowerKey;
        }

        return BinaryKeyComparer.Instance.Compare(left.Key, right.Key);
    }
}
