using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests;

/// <summary>Cancels the original caller only after real owned work reports the required progress.</summary>
internal sealed class ObservedWorkCancellation : IAsyncDisposable
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource lifetime = new(ObservationTimeout);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread observer;
    private readonly Func<long> measure;
    private readonly long minimumBytes;
    private readonly long baseline;
    private long observed;

    internal ObservedWorkCancellation(ZoneTreeStore store, CancellationTokenSource caller,
        Func<ZoneTreeReadSnapshot, long> measure, long minimumBytes = 1)
        : this(() => measure(store.GetReadDiagnostics()), caller, minimumBytes)
    {
    }

    internal ObservedWorkCancellation(Func<long> measure, CancellationTokenSource caller, long minimumBytes = 1)
    {
        this.measure = measure;
        this.minimumBytes = minimumBytes;
        baseline = measure();
        observed = baseline;
        observer = new(() => Observe(caller)) { IsBackground = true };
        observer.Start();
    }

    internal Task Ready => ready.Task;
    internal long ObservedProgress => Interlocked.Read(ref observed);
    internal bool CancelledAfterProgress => ObservedProgress - baseline >= minimumBytes;

    private void Observe(CancellationTokenSource caller)
    {
        try
        {
            ready.TrySetResult();
            while (!lifetime.IsCancellationRequested)
            {
                var bytes = measure();
                if (bytes - baseline >= minimumBytes)
                {
                    Interlocked.Exchange(ref observed, bytes);
                    caller.Cancel();
                    break;
                }
                Thread.SpinWait(64);
            }
            settled.TrySetResult();
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            ready.TrySetException(failure);
            settled.TrySetException(failure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        try
        {
            await settled.Task;
        }
        finally
        {
            observer.Join();
            lifetime.Dispose();
        }
    }
}
