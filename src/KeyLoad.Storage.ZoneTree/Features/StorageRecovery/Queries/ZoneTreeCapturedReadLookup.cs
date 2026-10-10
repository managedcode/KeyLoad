namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCapturedReadLookup
{
    private const int FirstRecordIndex = 0;
    private const int NeighborOffset = 1;
    private const int MidpointDivisor = 2;
    private const int EqualKeyComparison = 0;

    internal static int Find(List<ZoneTreeCapturedReadRecord> records, ReadOnlySpan<byte> key, int count)
    {
        var lower = FirstRecordIndex;
        var upper = count - NeighborOffset;
        while (lower <= upper)
        {
            var middle = lower + (upper - lower) / MidpointDivisor;
            var comparison = records[middle].Key.Span.SequenceCompareTo(key);
            if (comparison == EqualKeyComparison)
            { return middle; }
            if (comparison < EqualKeyComparison)
            { lower = middle + NeighborOffset; }
            else
            { upper = middle - NeighborOffset; }
        }
        return ~lower;
    }
}
