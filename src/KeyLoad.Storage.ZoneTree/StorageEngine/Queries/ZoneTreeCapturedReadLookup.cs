namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCapturedReadLookup
{
    private const int MidpointDivisor = 2;

    internal static int Find(List<ZoneTreeCapturedReadRecord> records, ReadOnlySpan<byte> key, int count)
    {
        var lower = 0;
        var upper = count - 1;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / MidpointDivisor;
            var comparison = records[middle].Key.Span.SequenceCompareTo(key);
            if (comparison == 0)
            {
                return middle;
            }
            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }
        return ~lower;
    }
}
