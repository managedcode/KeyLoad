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
    private readonly bool reverse;

    internal ZoneTreeBaselineCursor(IZoneTree<Memory<byte>, Memory<byte>> tree, byte[] lower, byte[]? upper,
        byte[] prefix, byte[]? afterKey, byte[]? untilKey, ZoneTreeRangeWork work, bool reverse)
    {
        var acquired = reverse
            ? tree.CreateReverseIterator(IteratorType.NoRefresh)
            : tree.CreateIterator(IteratorType.NoRefresh);
        try
        {
            if (reverse)
            {
                if (upper is not null)
                {
                    acquired.Seek(upper);
                }
            }
            else
            {
                acquired.Seek(lower);
            }
        }
        catch (Exception)
        {
            acquired.Dispose();
            throw;
        }
        iterator = acquired;
        this.prefix = prefix;
        this.afterKey = afterKey;
        this.untilKey = untilKey;
        this.work = work;
        this.reverse = reverse;
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
                if (ZoneTreeRangeBounds.IsPast(key.Span, prefix, afterKey, untilKey, reverse))
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
