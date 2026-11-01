namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRangeBounds
{
    internal static byte[]? Minimum(byte[]? first, byte[]? second)
    {
        if (first is null)
        {
            return second;
        }
        return second is null || BinaryKeyComparer.Instance.Compare(first, second) <= 0 ? first : second;
    }

    internal static bool Contains(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, byte[]? afterKey, byte[]? untilKey)
        => key.StartsWith(prefix) && (afterKey is null || key.SequenceCompareTo(afterKey) > 0)
            && (untilKey is null || key.SequenceCompareTo(untilKey) < 0);

    internal static bool IsPast(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, byte[]? afterKey,
        byte[]? untilKey, bool reverse)
        => reverse
            ? afterKey is not null && key.SequenceCompareTo(afterKey) <= 0
                || key.SequenceCompareTo(prefix) < 0
            : untilKey is not null && key.SequenceCompareTo(untilKey) >= 0
                || key.SequenceCompareTo(prefix) >= 0 && !key.StartsWith(prefix);
}
