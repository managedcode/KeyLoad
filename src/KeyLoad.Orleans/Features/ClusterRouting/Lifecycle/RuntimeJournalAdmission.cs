namespace KeyLoad.Orleans;

/// <summary>Opens native journal work only after replicated identity and catalog verification.</summary>
public sealed class RuntimeJournalAdmission : IDisposable
{
    private const string AdmissionClosed = "Runtime journal admission is closed.";
    private readonly Lock lifecycle = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource scheduling = new();

    internal CancellationToken SchedulingToken
    {
        get
        {
            lock (lifecycle)
            {
                return disposed ? new CancellationToken(canceled: true) : scheduling.Token;
            }
        }
    }

    internal bool IsReady { get { lock (lifecycle) { return !closed && ready.Task.IsCompletedSuccessfully; } } }

    internal void CloseScheduling()
    {
        lock (lifecycle)
        {
            if (!disposed)
            { scheduling.Cancel(); }
        }
    }
    private bool closed;
    private bool disposed;

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        CancellationToken stopToken;
        lock (lifecycle)
        {
            stopToken = disposed ? new CancellationToken(canceled: true) : stopping.Token;
        }
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);
        await ready.Task.WaitAsync(pending.Token).ConfigureAwait(false);
        pending.Token.ThrowIfCancellationRequested();
    }

    internal void Open(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (closed)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, AdmissionClosed);
            }
            ready.TrySetResult();
        }
    }

    internal void Close()
    {
        lock (lifecycle)
        {
            if (closed)
            {
                return;
            }
            closed = true;
            CloseScheduling();
            stopping.Cancel();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed)
            { return; }
            Close();
            stopping.Dispose();
            scheduling.Dispose();
            disposed = true;
        }
    }
}
