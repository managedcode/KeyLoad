namespace KeyLoad.Server.Features.Search;

/// <summary>Owns the original admitted operation including its initial canonical capture.</summary>
internal sealed class NativeTextIncrementalWorker : IAsyncDisposable
{
    private readonly Lock gate = new();
    private Task? active;
    private Task? disposal;
    private bool closed;

    internal async Task<T> RunAsync<T>(Func<T> operation)
    {
        Task<T> original;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (active is not null)
            { throw NativeTextErrors.BoundExceeded(); }
            original = Task.Run(operation, CancellationToken.None);
            active = original;
        }
        try
        { return await original.ConfigureAwait(false); }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(active, original))
                { active = null; }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource start;
        Task actual;
        lock (gate)
        {
            if (disposal is not null)
            { return new(disposal); }
            closed = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = JoinAsync(start.Task, active ?? Task.CompletedTask);
            disposal = actual;
        }
        start.TrySetResult();
        return new(actual);
    }

    private static async Task JoinAsync(Task start, Task original)
    {
        await start.ConfigureAwait(false);
        await original.ConfigureAwait(false);
    }
}
