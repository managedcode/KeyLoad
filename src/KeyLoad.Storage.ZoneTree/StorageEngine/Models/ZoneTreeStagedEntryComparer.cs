namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeStagedEntryComparer : IComparer<ZoneTreeStagedEntry>
{
    private const int LowerKey = -1;

    internal static ZoneTreeStagedEntryComparer Instance { get; } = new();

    public int Compare(ZoneTreeStagedEntry? left, ZoneTreeStagedEntry? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }
        if (left is null)
        {
            return LowerKey;
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
            return LowerKey;
        }

        return BinaryKeyComparer.Instance.Compare(left.Key, right.Key);
    }
}
