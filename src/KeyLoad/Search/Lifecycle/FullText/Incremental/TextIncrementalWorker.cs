namespace KeyLoad.Server.Features.Search;

/// <summary>Admits one maintenance operation at a time and joins the accepted operation before releasing its owner.</summary>
internal sealed class TextIncrementalWorker : IAsyncDisposable
{
    private readonly Lock gate = new();
    private TaskCompletionSource? active;
    private bool closed;

    /// <summary>Runs one operation now; a second concurrent operation is refused as a bound, never queued.</summary>
    internal async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        TaskCompletionSource original;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (active is not null)
            {
                throw TextErrors.BoundExceeded();
            }
            original = new(TaskCreationOptions.RunContinuationsAsynchronously);
            active = original;
        }
        try
        {
            return await operation();
        }
        finally
        {
            Release(original);
        }
    }

    /// <summary>Waits for the active operation, then runs cleanup as the only active work.</summary>
    internal async Task RunCleanupAsync(Func<Task> operation)
    {
        TaskCompletionSource original;
        while (true)
        {
            Task? pending;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                pending = active?.Task;
                if (pending is null)
                {
                    original = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    active = original;
                    break;
                }
            }
            await pending;
        }
        try
        {
            await operation();
        }
        finally
        {
            Release(original);
        }
    }

    private void Release(TaskCompletionSource original)
    {
        lock (gate)
        {
            original.TrySetResult();
            active = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closed = true;
            return new(active?.Task ?? Task.CompletedTask);
        }
    }
}
