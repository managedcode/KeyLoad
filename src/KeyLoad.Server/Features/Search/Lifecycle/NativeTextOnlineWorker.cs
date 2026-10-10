namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineWorker : IAsyncDisposable
{
    private readonly Lock gate = new();
    private Task? active;
    private Task? disposal;
    private bool closed;

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        Task<T> original;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (active is not null)
            { throw NativeTextErrors.Busy(); }
            original = Task.Run(operation, CancellationToken.None);
            active = original;
        }
        try
        { return await original.ConfigureAwait(false); }
        finally
        {
            lock (gate)
            { if (ReferenceEquals(active, original)) { active = null; } }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closed = true;
            disposal ??= JoinAsync(active ?? Task.CompletedTask);
            return new(disposal);
        }
    }

    private static async Task JoinAsync(Task original)
        => await original.ConfigureAwait(false);
}
