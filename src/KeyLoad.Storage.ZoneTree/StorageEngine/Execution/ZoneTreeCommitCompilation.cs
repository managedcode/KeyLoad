using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Compiles staged writes against the owning native transaction snapshot.</summary>
internal static class ZoneTreeCommitCompilation
{
    internal static (T Result, StorageMutation[] Changes, long Position) Compile<T>(ZoneTreeStoreRuntime runtime,
        Func<IAtomicTransaction, long, T> compile)
    {
        ArgumentNullException.ThrowIfNull(compile) /* KLD0041: BCL null validation accepts the compile Func reference through its object parameter. */;
        runtime.CurrentOperation.Check();
        runtime.Check();
        var transaction = new ZoneTreeTransaction(runtime);
        // The native write identity is known before publication and is covered by its eventual commit timestamp.
        // It is not a dense counter or a committed read cut.
        var native = (ITransaction<Memory<byte>, Memory<byte>>)runtime.CurrentOperation.Snapshot.Transaction;
        var position = native.TransactionId;
        T result;
        try
        {
            result = compile(transaction, position);
        }
        finally
        {
            transaction.EndPreparationContext();
        }
        return (result, transaction.PrepareChanges(), position);
    }
}
