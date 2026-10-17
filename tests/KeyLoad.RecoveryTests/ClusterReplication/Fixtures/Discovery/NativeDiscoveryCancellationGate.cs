using KeyLoad.Clustering;

namespace KeyLoad.RecoveryTests;

internal sealed class NativeDiscoveryCancellationGate : IReplicaNativeDiscoveryObserver
{
    private const int NotEntered = 0;
    private const int EnteredOnce = 1;
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int observed;

    internal Task Entered => entered.Task;
    internal void Release() => released.TrySetResult();

    public ReadOnlyMemory<byte> TransformNativeDiscovery(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery state, ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return originalPayload;
    }

    public async ValueTask<ReadOnlyMemory<byte>> TransformNativeDiscoveryAsync(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery state, ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref observed, EnteredOnce) == NotEntered)
        {
            entered.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return originalPayload;
    }
}
