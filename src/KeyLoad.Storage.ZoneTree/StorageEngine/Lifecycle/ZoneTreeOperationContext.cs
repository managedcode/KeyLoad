using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Binds the synchronous storage callback and its position to the native transaction's snapshot.</summary>
internal sealed class ZoneTreeOperationContext : IDisposable
{
    private const string NestedOperation = "A storage operation cannot enter another operation on the same owner.";
    private const string ExpiredOperation = "The native storage operation is outside its owning callback.";
    private readonly ZoneTreeStoreRuntime runtime;
    private bool active = true;

    internal ZoneTreeOperationContext(ZoneTreeStoreRuntime runtime, IReadTransaction<Memory<byte>, Memory<byte>> transaction,
        bool ownsTransaction)
    {
        this.runtime = runtime;
        Snapshot = new(transaction, ownsTransaction);
        try
        {
            if (runtime.Operation.Value is not null)
            {
                throw new InvalidOperationException(NestedOperation);
            }
            runtime.Check();
            Position = ZoneTreeStorePosition.Read(transaction);
            runtime.Operation.Value = this;
        }
        catch (Exception original)
        {
            try
            {
                Snapshot.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    internal ZoneTreeNativeSnapshot Snapshot { get; }
    internal bool Writable => Snapshot.Transaction is ITransaction<Memory<byte>, Memory<byte>>;
    internal long Position { get; private set; }
    internal void Published(long position) => Position = position;

    internal void Check()
    {
        if (!active)
        {
            throw new InvalidOperationException(ExpiredOperation);
        }
    }

    public void Dispose()
    {
        active = false;
        runtime.Operation.Value = null;
        Snapshot.Dispose();
    }
}
