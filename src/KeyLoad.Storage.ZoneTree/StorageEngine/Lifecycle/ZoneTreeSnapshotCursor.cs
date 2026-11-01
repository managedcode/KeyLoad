using ZoneTree;
using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns one native MVCC snapshot and its single traversal cursor.</summary>
internal sealed class ZoneTreeSnapshotCursor : IDisposable
{
    private readonly ZoneTreeNativeSnapshot snapshot;
    private bool iteratorDisposed;
    private bool snapshotDisposed;

    internal ZoneTreeSnapshotCursor(ZoneTreeStoreRuntime runtime, bool reverse = false, bool capturePlain = false)
    {
        snapshot = runtime.CurrentOperation.Snapshot.Retain();
        try
        {
            var options = new TransactionIteratorOptions { ContributeToTheBlockCache = false };
            var transactional = reverse ? snapshot.Transaction.CreateReverseIterator(options) : snapshot.Transaction.CreateIterator(options);
            try
            {
                var mode = capturePlain ? IteratorType.Snapshot : IteratorType.NoRefresh;
                var plain = reverse ? runtime.PlainTree.CreateReverseIterator(mode) : runtime.PlainTree.CreateIterator(mode);
                Iterator = new ZoneTreeMergedIterator(transactional, plain, reverse);
            }
            catch (Exception original)
            {
                try
                {
                    transactional.Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(original, cleanup);
                }
                throw;
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
    }

    internal ITransactionIterator<Memory<byte>, Memory<byte>> Iterator { get; }

    public void Dispose()
    {
        if (!iteratorDisposed)
        {
            Iterator.Dispose();
            iteratorDisposed = true;
        }
        if (!snapshotDisposed)
        {
            snapshot.Dispose();
            snapshotDisposed = true;
        }
    }
}
