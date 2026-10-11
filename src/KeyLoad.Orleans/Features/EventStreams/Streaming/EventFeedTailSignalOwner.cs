using Orleans.Streams;

namespace KeyLoad.Orleans;

internal sealed class EventFeedTailSignalOwner(Guid mapId) : IAsyncObserver<EventFeedWakeupHint>, IDisposable
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const string InvalidHint = "The native event feed advisory is inconsistent.";
    private readonly Lock gate = new();
    private TaskCompletionSource<bool> signal = CreateSignal();
    private Exception? originalFailure;
    private bool completed;
    private bool disposed;

    internal Task CaptureSignal()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var original = signal.Task;
            if (original.IsCompleted && !completed)
            { signal = CreateSignal(); }
            return original;
        }
    }

    internal (bool Completed, Exception? OriginalFailure) State()
    {
        lock (gate)
        { return (completed, originalFailure); }
    }

    public Task OnNextAsync(EventFeedWakeupHint item, StreamSequenceToken? token = null)
    {
        lock (gate)
        {
            if (disposed || completed)
            { return Task.CompletedTask; }
            if (item is null || item.Version != Version || item.MapId != mapId
                || item.MapRevision < FirstRevision || item.CoverageGeneration < FirstRevision)
            {
                originalFailure = Errors.Fail(ErrorCode.Corruption, InvalidHint);
                completed = true;
            }
            signal.TrySetResult(true);
        }
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync()
    {
        lock (gate)
        {
            if (!disposed)
            { completed = true; signal.TrySetResult(true); }
        }
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate)
        {
            if (!disposed && !completed)
            {
                originalFailure = exception;
                completed = true;
                signal.TrySetResult(true);
            }
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            completed = true;
            signal.TrySetResult(true);
        }
    }

    private static TaskCompletionSource<bool> CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
