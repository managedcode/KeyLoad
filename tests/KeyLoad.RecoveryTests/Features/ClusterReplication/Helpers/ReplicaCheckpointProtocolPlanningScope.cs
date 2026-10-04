using System.Collections.Immutable;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaCheckpointProtocolPlanningScope
{
    private readonly ReplicaCheckpointProtocolGateNode node;
    private readonly CancellationToken cancellationToken;
    private readonly TimeSpan timeout;
    private readonly TaskCompletionSource<ReplicaCheckpointProtocolPlanningObservation> entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task<ReplicaCheckpointProtocolPlanningObservation> completion;

    internal ReplicaCheckpointProtocolPlanningScope(ReplicaCheckpointProtocolGateNode node,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        this.node = node;
        this.cancellationToken = cancellationToken;
        this.timeout = timeout;
        var state = new ReplicaState(node.Materializer, node.Configuration, TimeProvider.System);
        completion = Task.Run(() => state.LockedAsync(CaptureAndHold, this.cancellationToken), CancellationToken.None);
    }

    internal async Task<ReplicaCheckpointProtocolPlanningObservation> WaitForEntryAsync(CancellationToken token)
    {
        var completed = await Task.WhenAny(entered.Task, completion).WaitAsync(token);
        return completed == completion ? await completion : await entered.Task;
    }

    internal void Release() => released.TrySetResult();

    internal Task<ReplicaCheckpointProtocolPlanningObservation> JoinAsync() => completion;

    private ReplicaCheckpointProtocolPlanningObservation CaptureAndHold()
    {
        var observation = new ReplicaCheckpointProtocolPlanningObservation(node.Log.State, node.Log.TermAt(1),
            node.Log.Read(2, node.Configuration.MaxAppendEntries, node.Configuration.MaxAppendBytes));
        entered.TrySetResult(observation);
        released.Task.WaitAsync(timeout, TimeProvider.System, cancellationToken).GetAwaiter().GetResult();
        return observation;
    }
}

internal sealed record ReplicaCheckpointProtocolPlanningObservation(ReplicaHardState State, long Term,
    ImmutableArray<ReplicaEntry> Suffix);
