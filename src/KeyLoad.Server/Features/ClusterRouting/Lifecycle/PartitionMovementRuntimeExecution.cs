using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Runs actual admitted invocations with the runtime's original gate, active set and shutdown token.</summary>
internal sealed class PartitionMovementRuntimeExecution(Lock gate, CancellationTokenSource stopping,
    Dictionary<Guid, TaskCompletionSource> active, NativeRequestWorkOwner work, Func<bool> isClosed)
{
    internal async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken caller)
    {
        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (isClosed())
            { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMovementProtocol.Unavailable); }
            active.Add(id, completion);
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
            using var frame = new NativeCapabilityWorkLifetime(linked.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                frame.Admit(work, id, NativeRequestWorkKind.CommandCapability);
                await operation(frame.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        lock (gate)
        {
            completion.TrySetResult();
            active.Remove(id);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
