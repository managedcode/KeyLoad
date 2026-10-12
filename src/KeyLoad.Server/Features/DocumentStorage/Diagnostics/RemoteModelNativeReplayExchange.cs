namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteModelNativeReplayExchange(RemoteModelNativeClient client,
    RegisteredPhysicalOwnerV1 destination, RemoteModelNativeRequest original, long maximumReplyBytes,
    RemoteModelNativeReplayVariation permitted, CancellationToken originalToken) : IAsyncDisposable
{
    private const int NextWitnessOrdinal = 1;
    private readonly Lock sync = new();
    private readonly CancellationTokenSource closed = new();
    private Task<RemoteModelNativeReplayResult>? running;
    private Task? shutdown;
    private int count;
    private bool active;
    internal bool InvokedEffects { get; private set; }

    internal Task<RemoteModelNativeReplayResult> ExecuteAsync(RemoteModelNativeReplayVariation variation,
        CancellationToken caller)
    {
        TaskCompletionSource start;
        Task<RemoteModelNativeReplayResult> result;
        lock (sync)
        {
            var limit = RemoteModelNativeReplayCatalog.Count(permitted);
            if (shutdown is not null || active || count >= limit || variation != RemoteModelNativeReplayCatalog.At(permitted, count + NextWitnessOrdinal))
            { throw new InvalidOperationException(RemoteModelNativeProtocol.InvalidProof); }
            originalToken.ThrowIfCancellationRequested();
            caller.ThrowIfCancellationRequested();
            count++;
            active = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            running = RunAsync(start.Task, variation, caller);
            result = running;
        }
        start.SetResult();
        return result;
    }

    private async Task<RemoteModelNativeReplayResult> RunAsync(Task start,
        RemoteModelNativeReplayVariation variation, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(originalToken, caller, closed.Token);
        try
        { return await client.ReplayCapturedAsync(destination, original, maximumReplyBytes, variation, this, linked.Token).ConfigureAwait(false); }
        finally
        { lock (sync) { active = false; } }
    }

    internal void MarkInvoked(RemoteModelNativeReplayVariation variation)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null || !active
                || variation != RemoteModelNativeReplayCatalog.At(permitted, count), this);
            InvokedEffects |= variation == RemoteModelNativeReplayVariation.Original2;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        { return new(shutdown ??= JoinAsync(running)); }
    }

    private async Task JoinAsync(Task? admitted)
    {
        try
        {
            await closed.CancelAsync().ConfigureAwait(false);
            if (admitted is not null)
            { await admitted.ConfigureAwait(false); }
        }
        finally
        { closed.Dispose(); }
    }
}
