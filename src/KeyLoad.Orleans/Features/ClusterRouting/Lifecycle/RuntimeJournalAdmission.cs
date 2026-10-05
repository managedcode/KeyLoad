namespace KeyLoad.Orleans;

/// <summary>Opens native journal work only after replicated identity and catalog verification.</summary>
public sealed class RuntimeJournalAdmission : IDisposable
{
    private readonly Lock lifecycle = new();
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource scheduling = new();

    internal CancellationToken SchedulingToken => scheduling.Token;

    internal void CloseScheduling() => scheduling.Cancel();
    private bool closed;

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
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
                throw Errors.Fail(ErrorCode.OwnershipLost, "Runtime journal admission is closed.");
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
        }
        CloseScheduling();
        stopping.Cancel();
    }

    public void Dispose()
    {
        Close();
        stopping.Dispose();
        scheduling.Dispose();
    }
}
