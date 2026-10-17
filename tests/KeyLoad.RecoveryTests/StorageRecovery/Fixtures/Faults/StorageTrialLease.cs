namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class StorageTrialLease : IDisposable
{
    private static readonly SemaphoreSlim TrialSlots = new(MaximumConcurrentStorageTrials, MaximumConcurrentStorageTrials);
    private static int activeStorageTrials;
    private static int maximumObservedStorageTrials;
    private int disposed;

    private StorageTrialLease()
    {
    }

    internal const int MaximumConcurrentStorageTrials = 4;

    internal static int ActiveStorageTrials => Volatile.Read(ref activeStorageTrials);

    internal static int MaximumObservedStorageTrials => Volatile.Read(ref maximumObservedStorageTrials);

    internal static async ValueTask<StorageTrialLease> AcquireAsync(CancellationToken cancellationToken)
    {
        var lease = new StorageTrialLease();
        await TrialSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var active = Interlocked.Increment(ref activeStorageTrials);
        ObserveMaximum(active);
        return lease;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Interlocked.Decrement(ref activeStorageTrials);
        TrialSlots.Release();
    }

    private static void ObserveMaximum(int active)
    {
        var observed = Volatile.Read(ref maximumObservedStorageTrials);
        while (active > observed)
        {
            var prior = Interlocked.CompareExchange(ref maximumObservedStorageTrials, active, observed);
            if (prior == observed)
            {
                return;
            }

            observed = prior;
        }
    }
}
