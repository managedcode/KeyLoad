using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests;

/// <summary>Executes original database operations against one real store with synchronous native point observation.</summary>
internal sealed class PointObservedStore(IAtomicStore original, Action afterRead) : IAtomicStore
{
    private Action? observation = afterRead;
    public StoreIdentity Identity => original.Identity;
    public long Position => original.Position;

    public T Read<T>(Func<IKeyValueView, T> read)
        => original.Read(view => read(new PointObservedView(view, Observe)));

    public ValueTask<T> ReadAsync<T>(Func<IKeyValueView, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken = default)
        => original.ReadAsync((view, token) => read(new PointObservedView(view, Observe), token), cancellationToken);

    public T Prepare<T>(Func<IAtomicTransaction, long, T> prepare) => original.Prepare(prepare);
    public T Commit<T>(Func<IAtomicTransaction, long, T> compile) => original.Commit(compile);

    public Task<T> CommitAsync<T>(Func<IAtomicTransaction, long, T> compile, CancellationToken cancellationToken = default)
        => original.CommitAsync(compile, cancellationToken);

    public Task SyncAsync(CancellationToken cancellationToken = default) => original.SyncAsync(cancellationToken);

    public Task<long> CreateBackupAsync(string directory, CancellationToken cancellationToken = default)
        => original.CreateBackupAsync(directory, cancellationToken);

    /// <summary>Stops this observation; the original fixture retains physical ownership.</summary>
    public void Dispose() => Interlocked.Exchange(ref observation, null);

    private void Observe() => Volatile.Read(ref observation)?.Invoke();
}
