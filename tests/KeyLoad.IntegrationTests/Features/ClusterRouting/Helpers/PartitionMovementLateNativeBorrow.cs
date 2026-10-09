using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Finite in-process retention, never an authority issuer or original request execution path.</summary>
internal sealed class PartitionMovementLateNativeBorrow
{
    private readonly Lock gate = new();
    private TaskCompletionSource<PartitionMovementLateNativeCaptured>? next;
    private readonly TaskCompletionSource firstJoined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource secondJoined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int admitted;
    private const int First = 1;
    private const int Second = 2;

    internal Task<PartitionMovementLateNativeCaptured> Arm()
    {
        lock (gate)
        {
            if (next is not null)
            { throw new InvalidOperationException("A sealed native borrow is already armed."); }
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return next.Task;
        }
    }

    internal IGrainPartitionMovementSealedOperationObserver ForOwner(int index) => new Owner(this, index);

    private async ValueTask AcceptAsync(int index, ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        TaskCompletionSource<PartitionMovementLateNativeCaptured>? selected;
        int ordinal;
        lock (gate)
        {
            selected = next;
            if (selected is null)
            { return; }
            next = null;
            ordinal = ++admitted;
        }
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        if (phase.Stage != PartitionMovePeerStage.Retire || phase.ReceiverEffectAdmission is null)
        { throw new InvalidOperationException("The actual parent receiver did not seal Retire authority."); }
        if (ordinal is not (First or Second))
        { throw new InvalidOperationException("The finite native borrow exceeded its two producers."); }
        selected.SetResult(new(index, operation));
        try
        { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (ordinal == First)
            { firstJoined.TrySetResult(); }
            else if (ordinal == Second)
            { secondJoined.TrySetResult(); }

        }
    }

    internal Task JoinFirstAsync(CancellationToken cancellationToken) => firstJoined.Task.WaitAsync(cancellationToken);
    internal Task JoinSecondAsync(CancellationToken cancellationToken) => secondJoined.Task.WaitAsync(cancellationToken);
    private sealed class Owner(PartitionMovementLateNativeBorrow owner, int index) : IGrainPartitionMovementSealedOperationObserver
    {
        public ValueTask BorrowAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
            => owner.AcceptAsync(index, operation, cancellationToken);
    }
}

internal sealed record PartitionMovementLateNativeCaptured(int OwnerIndex, ReplicatedOperation Operation);
