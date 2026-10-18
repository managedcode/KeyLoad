namespace KeyLoad.Server.Features.Search;

/// <summary>Bounds actual asynchronous publication and joins accepted work during owner shutdown.</summary>
internal sealed class NativeTextOnlineWorker : IAsyncDisposable
{
    private readonly Lock gate = new();
    private TaskCompletionSource? active;
    private bool closed;

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        TaskCompletionSource original;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (active is not null)
            {
                throw NativeTextErrors.Busy();
            }
            original = new(TaskCreationOptions.RunContinuationsAsynchronously);
            active = original;
        }
        try
        {
            return await operation().ConfigureAwait(false);
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
