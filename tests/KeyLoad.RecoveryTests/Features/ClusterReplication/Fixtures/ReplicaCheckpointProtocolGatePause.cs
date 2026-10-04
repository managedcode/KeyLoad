using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaCheckpointProtocolGatePause
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int armed;

    internal Task Entered => entered.Task;
    internal void Arm() => Volatile.Write(ref armed, 1);
    internal void Release() => released.TrySetResult();

    internal void Observe(CommitStage stage, long _, int _1)
    {
        if (stage != CommitStage.SnapshotFlushed || Interlocked.Exchange(ref armed, 0) == 0)
        {
            return;
        }
        entered.TrySetResult();
        released.Task.GetAwaiter().GetResult();
    }
}
