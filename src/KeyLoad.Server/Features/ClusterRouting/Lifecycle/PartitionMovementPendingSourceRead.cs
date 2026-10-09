using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Retains the original capture producer and its independently owned shutdown token.</summary>
internal class PartitionMovementPendingSourceRead<T>(PartitionRef partition, Guid moveId)
    : IPartitionMovementPendingSourceRead, IDisposable where T : class
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private Task? cancellation;
    private bool disposed;
    public PartitionRef Partition { get; } = partition;
    public Guid MoveId { get; } = moveId;
    internal CancellationToken StageCancellation => stopping.Token;
    public Task CompletionTask => Completion.Task;
    internal TaskCompletionSource<T> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task StopAsync()
    {
        lock (gate)
        { return disposed ? Task.CompletedTask : cancellation ??= stopping.CancelAsync(); }
    }

    public void Dispose()
    {
        Task? original;
        lock (gate)
        {
            if (disposed)
            { return; }
            disposed = true;
            original = cancellation;
        }
        var failures = new List<Exception>();
        if (original is not null)
        { ServerFailureObserver.Observe(() => original.GetAwaiter().GetResult(), failures); }
        try
        { stopping.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
