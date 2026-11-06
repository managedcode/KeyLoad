namespace KeyLoad.Clustering;

/// <summary>Wakes prefix waiters only when their logical owner's persisted-copy knowledge advances.</summary>
internal sealed class StorageCopyProgress
{
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Changed => changed.Task;

    internal void Advance()
    {
        var previous = changed;
        changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    internal void Stop() => changed.TrySetCanceled();
}
