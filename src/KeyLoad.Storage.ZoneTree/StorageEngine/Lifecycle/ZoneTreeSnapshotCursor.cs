using ZoneTree;
using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns one native MVCC snapshot and its single traversal cursor.</summary>
internal sealed class ZoneTreeSnapshotCursor : IDisposable
{
    private readonly IReadTransaction<Memory<byte>, Memory<byte>> snapshot;
    private bool iteratorDisposed;
    private bool snapshotDisposed;

    internal ZoneTreeSnapshotCursor(ITransactionalZoneTree<Memory<byte>, Memory<byte>> tree, bool reverse = false)
    {
        snapshot = tree.BeginReadOnlyTransaction();
        try
        {
            var options = new TransactionIteratorOptions { ContributeToTheBlockCache = false };
            Iterator = reverse ? snapshot.CreateReverseIterator(options) : snapshot.CreateIterator(options);
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
