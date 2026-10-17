namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Starts the one original cleanup deadline lazily and never resets it between children.</summary>
internal sealed class ControlledPartitionMovementCleanupDeadline(NativeMovementProcessOptions options,
    TimeProvider clock) : IDisposable
{
    private CancellationTokenSource? deadline;
    internal CancellationToken Token => (deadline ??= new(options.CleanupTimeout, clock)).Token;
    public void Dispose() => deadline?.Dispose();
}
