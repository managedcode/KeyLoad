using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests;

/// <summary>Executes original database operations with synchronous observation of completed native reads.</summary>
internal sealed class ReadObservedStore(IAtomicStore original, Action? afterRead = null, Action? afterRange = null) : IAtomicStore
{
    private Action? observation = afterRead;
    private Action? rangeObservation = afterRange;
    private long observedProgress;
    private int cancelledAfterProgress;
    internal long ObservedProgress => Interlocked.Read(ref observedProgress);
    internal bool CancelledAfterProgress => Volatile.Read(ref cancelledAfterProgress) != 0;
    public StoreIdentity Identity => original.Identity;
    public long Position => original.Position;

    /// <summary>Arms cancellation at an actual native read boundary after the original counter advances.</summary>
    internal void CancelAfterReadProgress(CancellationTokenSource caller, Func<long> measure,
        Action? onProgress = null)
    {
        var baseline = measure();
        Interlocked.Exchange(ref observedProgress, baseline);
        Interlocked.Exchange(ref cancelledAfterProgress, 0);
        void Progress()
        {
            var current = measure();
            if (current <= baseline || Interlocked.CompareExchange(ref cancelledAfterProgress, 1, 0) != 0)
            {
                return;
            }
            Interlocked.Exchange(ref observedProgress, current);
            onProgress?.Invoke();
            caller.Cancel();
        }
        observation = Progress;
        rangeObservation = Progress;
    }

    public T Read<T>(Func<IKeyValueView, T> read)
        => original.Read(view => read(new ReadObservedView(view, Observe, ObserveRange)));

    public ValueTask<T> ReadAsync<T>(Func<IKeyValueView, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken = default)
        => original.ReadAsync((view, token) => read(new ReadObservedView(view, Observe, ObserveRange), token), cancellationToken);

    public T Prepare<T>(Func<IAtomicTransaction, long, T> prepare) => original.Prepare(prepare);
    public T Commit<T>(Func<IAtomicTransaction, long, T> compile) => original.Commit(compile);

    public Task<T> CommitAsync<T>(Func<IAtomicTransaction, long, T> compile, CancellationToken cancellationToken = default)
        => original.CommitAsync(compile, cancellationToken);

    public Task SyncAsync(CancellationToken cancellationToken = default) => original.SyncAsync(cancellationToken);

    public Task<long> CreateBackupAsync(string directory, CancellationToken cancellationToken = default)
        => original.CreateBackupAsync(directory, cancellationToken);

    /// <summary>Stops this observation; the original fixture retains physical ownership.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref observation, null);
        Interlocked.Exchange(ref rangeObservation, null);
    }

    private void Observe() => Volatile.Read(ref observation)?.Invoke();
    private void ObserveRange() => Volatile.Read(ref rangeObservation)?.Invoke();
}
