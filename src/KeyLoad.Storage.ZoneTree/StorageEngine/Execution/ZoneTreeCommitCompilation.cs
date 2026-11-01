namespace KeyLoad.Storage.ZoneTree;

/// <summary>Compiles staged writes against the owning native transaction snapshot.</summary>
internal static class ZoneTreeCommitCompilation
{
    internal static (T Result, StorageMutation[] Changes, long Position) Compile<T>(ZoneTreeStoreRuntime runtime,
        Func<IAtomicTransaction, long, T> compile)
    {
        ArgumentNullException.ThrowIfNull(compile);
        runtime.CurrentOperation.Check();
        runtime.Check();
        var transaction = new ZoneTreeTransaction(runtime);
        var position = checked(runtime.Position + 1);
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
