namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRangeBounds
{
    private const int PrefixByteWidth = 1;
    private const int FirstPrefixByte = 0;
    private const int EqualKeys = 0;

    internal static byte[]? PrefixSuccessor(ReadOnlySpan<byte> prefix)
    {
        if (prefix.IsEmpty)
        {
            return null;
        }

        var successor = prefix.ToArray();
        for (var index = successor.Length - PrefixByteWidth; index >= FirstPrefixByte; index--)
        {
            if (successor[index] == byte.MaxValue)
            {
                continue;
            }

            successor[index]++;
            return successor[..(index + PrefixByteWidth)];
        }

        return null;
    }

    internal static byte[]? Minimum(byte[]? first, byte[]? second)
    {
        if (first is null)
        {
            return second;
        }
        return second is null || BinaryKeyComparer.Instance.Compare(first, second) <= EqualKeys ? first : second;
    }

    internal static bool Contains(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, byte[]? afterKey, byte[]? untilKey)
        => key.StartsWith(prefix) && (afterKey is null || key.SequenceCompareTo(afterKey) > EqualKeys)
            && (untilKey is null || key.SequenceCompareTo(untilKey) < EqualKeys);

    internal static bool IsPast(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, byte[]? afterKey,
        byte[]? untilKey, bool reverse)
        => reverse
            ? afterKey is not null && key.SequenceCompareTo(afterKey) <= EqualKeys
                || key.SequenceCompareTo(prefix) < EqualKeys
            : untilKey is not null && key.SequenceCompareTo(untilKey) >= EqualKeys
                || key.SequenceCompareTo(prefix) >= EqualKeys && !key.StartsWith(prefix);
}
