using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class ReplicaTransportFailureGate(string message) : IReplicaNativeDiscoveryObserver
{
    internal Exception Failure { get; } = new InvalidOperationException(message);
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Release() => release.TrySetResult();

    private void FailCancellation()
    {
        Cancelled.TrySetResult();
        throw Failure;
    }

    public ReadOnlyMemory<byte> TransformNativeDiscovery(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery state, ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return originalPayload;
    }

    public async ValueTask<ReadOnlyMemory<byte>> TransformNativeDiscoveryAsync(ReplicaNativeDiscoveryRequest request,
        ReplicaSiloDiscovery state, ReadOnlyMemory<byte> originalPayload, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(FailCancellation);
        Entered.TrySetResult();
        try
        {
            await release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return originalPayload;
        }
        finally
        {
            Exited.TrySetResult();
        }
    }

    internal async Task<OperationResult> ForwardAsync(DatabaseEngine database, ReplicatedOperation operation,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(FailCancellation);
        Entered.TrySetResult();
        try
        {
            await release.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return database.Apply(operation);
        }
        finally
        {
            Exited.TrySetResult();
        }
    }
}
