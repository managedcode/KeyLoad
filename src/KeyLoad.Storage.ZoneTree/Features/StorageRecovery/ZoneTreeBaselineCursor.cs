using ZoneTree;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeBaselineCursor : IDisposable
{
    private readonly IZoneTreeIterator<Memory<byte>, Memory<byte>> iterator;
    private readonly byte[] prefix;
    private readonly byte[]? afterKey;
    private readonly byte[]? untilKey;
    private readonly ZoneTreeRangeWork work;

    internal ZoneTreeBaselineCursor(IZoneTree<Memory<byte>, Memory<byte>> tree, byte[] lower,
        byte[] prefix, byte[]? afterKey, byte[]? untilKey, ZoneTreeRangeWork work)
    {
        iterator = tree.CreateIterator(IteratorType.NoRefresh);
        iterator.Seek(lower);
        this.prefix = prefix;
        this.afterKey = afterKey;
        this.untilKey = untilKey;
        this.work = work;
    }

    internal Memory<byte> Key { get; private set; }
    internal ReadOnlyMemory<byte> Value { get; private set; }

    internal bool MoveNext()
    {
        while (true)
        {
            work.Check();
            if (!iterator.Next())
            {
                return false;
            }

            var key = iterator.CurrentKey;
            if (!ZoneTreeRangeBounds.Contains(key.Span, prefix, afterKey, untilKey))
            {
                if (ZoneTreeRangeBounds.IsPast(key.Span, prefix, untilKey))
                {
                    return false;
                }
                continue;
            }

            Key = key;
            Value = iterator.CurrentValue[StorageValueHeaderBytes..];
            work.ChargeBaseline((long)key.Length + Value.Length);
            return true;
        }
    }

    public void Dispose() => iterator.Dispose();
}
