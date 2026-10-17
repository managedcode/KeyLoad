namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePhaseWorkers(CancellationToken cellCancellation) : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cellCancellation);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task[] tasks = new Task[TimeSeriesIntensiveProfile.Concurrency];
    private readonly System.Threading.Lock closeGate = new();
    private int count;
    private int workersStarted;
    private Task? joined;
    private Task? closing;

    internal CancellationToken Token => cancellation.Token;
    internal bool IsCancellationRequested => cancellation.IsCancellationRequested;
    internal int WorkersStarted => Volatile.Read(ref workersStarted);
    internal Task Ready => ready.Task;
    internal Task Start => start.Task;

    internal void Add(Task task) => tasks[count++] = task;
    internal void Release() => start.TrySetResult();
    internal Task JoinAsync() => joined ??= Task.WhenAll(tasks.Take(count));

    internal void SignalReady()
    {
        if (Interlocked.Increment(ref workersStarted) == TimeSeriesIntensiveProfile.Concurrency)
        {
            ready.SetResult();
        }
    }

    internal async Task CancelAfterFatalAsync(Exception primary)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception cancellationFailure)
        {
            throw new AggregateException(primary, cancellationFailure);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (closeGate)
        {
            closing ??= CloseAsync();
            return new(closing);
        }
    }

    private async Task CloseAsync()
    {
        const int SingleItemCount = 1;

        var cancelled = cancellation.CancelAsync();
        Release();
        var completion = Task.WhenAll(JoinAsync(), cancelled);
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (completion.Exception is { InnerExceptions.Count: > SingleItemCount } failures)
            {
                throw failures;
            }

            throw;
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
