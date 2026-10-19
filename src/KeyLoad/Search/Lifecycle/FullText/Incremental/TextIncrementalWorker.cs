namespace KeyLoad.Server.Features.Search;

/// <summary>Bounds synchronous execution work and joins accepted work before releasing its owner.</summary>
internal sealed class TextIncrementalWorker : IAsyncDisposable
{
    private readonly Lock gate = new();
    private TaskCompletionSource? active;
    private bool closed;

    internal T Run<T>(Func<T> operation)
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
            return operation();
        }
        finally
        {
            lock (gate)
            {
                original.TrySetResult();
                active = null;
            }
        }
    }

    internal async Task RunCleanupAsync(Action operation)
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
            operation();
        }
        finally
        {
            lock (gate)
            {
                original.TrySetResult();
                active = null;
            }
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
