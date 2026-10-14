using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PortableMovementBorrow : IGrainPartitionMovementSealedOperationObserver
{
    private readonly TaskCompletionSource<ReplicatedOperation> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int selected;

    internal Task<ReplicatedOperation> Entered => entered.Task;
    internal bool IsSelected => Volatile.Read(ref selected) != 0;
    internal void Release() => released.TrySetResult();
    internal Task JoinAsync() => IsSelected ? joined.Task : Task.CompletedTask;

    public async ValueTask BorrowAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        if (phase.Stage != PartitionMovePeerStage.Retire || phase.ReceiverEffectAdmission is null)
        {
            throw new InvalidOperationException("The original native Retire authority is absent.");
        }
        if (Interlocked.CompareExchange(ref selected, 1, 0) != 0)
        {
            return;
        }
        try
        {
            entered.SetResult(operation);
            await released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            joined.TrySetResult();
        }
    }
}
