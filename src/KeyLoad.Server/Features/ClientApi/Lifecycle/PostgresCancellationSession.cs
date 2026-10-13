namespace KeyLoad.Server;

internal sealed class PostgresCancellationSession
{
    private readonly Lock gate = new();
    private CancellationTokenSource? active;
    private Task? cancel;
    private bool closing;

    internal CancellationToken Begin(CancellationToken closed)
    {
        lock (gate)
        {
            if (closing || active is not null)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerConnectionProtocol.Closed); }
            active = CancellationTokenSource.CreateLinkedTokenSource(closed);
            cancel = null;
            return active.Token;
        }
    }

    internal Task CancelAsync()
    {
        lock (gate)
        {
            return active is null ? Task.CompletedTask : cancel ??= active.CancelAsync();
        }
    }

    internal async Task EndAsync()
    {
        CancellationTokenSource? original;
        Task? originalCancel;
        lock (gate)
        {
            original = active;
            originalCancel = cancel;
            active = null;
            cancel = null;
        }
        var failures = new List<Exception>();
        if (originalCancel is not null)
        { await ServerFailureObserver.ObserveAsync(() => originalCancel, failures).ConfigureAwait(false); }
        if (original is not null)
        { ServerFailureObserver.Observe(original.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task CloseAsync()
    {
        lock (gate)
        { closing = true; }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(EndAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
