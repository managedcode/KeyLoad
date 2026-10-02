namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Observes actual report-file growth on a dedicated thread.</summary>
internal sealed class RealFileGrowthObserver : IAsyncDisposable
{
    private const int PollIntervalMilliseconds = 1;

    private readonly string path;
    private readonly Action cancelWriter;
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource armed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource growth = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task worker;
    private int disposed;

    private RealFileGrowthObserver(string path, Action cancelWriter)
    {
        this.path = path;
        this.cancelWriter = cancelWriter;
        worker = Task.Factory.StartNew(Observe, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal static async Task<RealFileGrowthObserver> StartAsync(string path, Action cancelWriter, CancellationToken startupToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(cancelWriter);
        var observer = new RealFileGrowthObserver(path, cancelWriter);
        try
        {
            await observer.armed.Task.WaitAsync(startupToken).ConfigureAwait(false);
            return observer;
        }
        catch (Exception)
        {
            await observer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal Task WaitForGrowthAsync(CancellationToken cancellationToken)
        => WaitForGrowthOrWorkerFailureAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await stop.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await worker.ConfigureAwait(false);
            }
            finally
            {
                stop.Dispose();
            }
        }
    }

    private void Observe()
    {
        armed.TrySetResult();
        while (!stop.IsCancellationRequested)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                cancelWriter();
                growth.TrySetResult();
                return;
            }

            Thread.Sleep(PollIntervalMilliseconds);
        }

        growth.TrySetCanceled(stop.Token);
    }

    private async Task WaitForGrowthOrWorkerFailureAsync(CancellationToken cancellationToken)
    {
        var completed = await Task.WhenAny(worker, growth.Task).WaitAsync(cancellationToken).ConfigureAwait(false);
        await completed.ConfigureAwait(false);
        await growth.Task.ConfigureAwait(false);
    }
}
