using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Shares the lifetime of one native snapshot with the read cursors retained by that operation.</summary>
internal sealed class ZoneTreeNativeSnapshot(IReadTransaction<Memory<byte>, Memory<byte>> transaction, bool ownsTransaction)
    : IDisposable
{
    private const int InitialReference = 1;
    private int references = InitialReference;
    internal IReadTransaction<Memory<byte>, Memory<byte>> Transaction => transaction;

    internal ZoneTreeNativeSnapshot Retain()
    {
        Interlocked.Increment(ref references);
        return this;
    }

    public void Dispose()
    {
        if (Interlocked.Decrement(ref references) == 0 && ownsTransaction)
        {
            try
            {
                transaction.Dispose();
            }
            catch (Exception)
            {
                Interlocked.Increment(ref references);
                throw;
            }
        }
    }
}
