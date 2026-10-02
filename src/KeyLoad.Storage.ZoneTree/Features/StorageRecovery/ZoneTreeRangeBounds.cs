namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeRangeBounds
{
    internal static bool Contains(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, byte[]? afterKey, byte[]? untilKey)
        => key.StartsWith(prefix) && (afterKey is null || key.SequenceCompareTo(afterKey) > 0)
            && (untilKey is null || key.SequenceCompareTo(untilKey) < 0);

    internal static bool IsPast(ReadOnlySpan<byte> key, ReadOnlySpan<byte> prefix, byte[]? untilKey)
        => (untilKey is not null && key.SequenceCompareTo(untilKey) >= 0)
            || key.SequenceCompareTo(prefix) >= 0 && !key.StartsWith(prefix);
}
