using ZoneTree;
using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeBaselineCursor : IDisposable
{
    private readonly ZoneTreeSnapshotCursor snapshot;
    private readonly ITransactionIterator<Memory<byte>, Memory<byte>> iterator;
    private readonly byte[] prefix;
    private readonly byte[]? afterKey;
    private readonly byte[]? untilKey;
    private readonly ZoneTreeRangeWork work;
    private readonly bool reverse;

    internal ZoneTreeBaselineCursor(ITransactionalZoneTree<Memory<byte>, Memory<byte>> tree, byte[] lower, byte[]? upper,
        byte[] prefix, byte[]? afterKey, byte[]? untilKey, ZoneTreeRangeWork work, bool reverse)
    {
        snapshot = new(tree, reverse);
        var acquired = snapshot.Iterator;
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
        catch (Exception original)
        {
            try
            {
                snapshot.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
        iterator = acquired;
        this.prefix = prefix;
        this.afterKey = afterKey;
        this.untilKey = untilKey;
        this.work = work;
        this.reverse = reverse;
    }

    internal Memory<byte> Key
    {
        get; private set;
    }
    internal ReadOnlyMemory<byte> Value
    {
        get; private set;
    }

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
            Value = iterator.CurrentValue;
            work.ChargeBaseline((long)key.Length + Value.Length);
            return true;
        }
    }

    public void Dispose() => snapshot.Dispose();
}
