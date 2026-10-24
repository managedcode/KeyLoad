namespace KeyLoad.Server.Search;

/// <summary>Bounds actual asynchronous publication and joins accepted work during owner shutdown.</summary>
internal sealed class TextOnlineWorker : IAsyncDisposable
{
    private readonly Lock gate = new();
    private TaskCompletionSource? active;
    private bool closed;

    internal Task<T> RunAsync<T>(Func<Task<T>> operation) => RunCoreAsync(operation, false);
    internal Task<T> RunCleanupAsync<T>(Func<Task<T>> operation) => RunCoreAsync(operation, true);

    private async Task<T> RunCoreAsync<T>(Func<Task<T>> operation, bool waitForAccepted)
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
                if (!waitForAccepted)
                {
                    throw TextErrors.Busy();
                }
            }
            await pending;
        }
        try
        {
            return await operation();
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
