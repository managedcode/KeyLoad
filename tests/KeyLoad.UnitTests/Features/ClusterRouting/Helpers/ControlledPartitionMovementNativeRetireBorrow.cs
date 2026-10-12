using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementNativeRetireBorrow
{
    private const int MaximumBorrows = 2;
    private readonly Lock gate = new();
    private TaskCompletionSource<ControlledPartitionMovementNativeRetireCaptured>? next;
    private readonly TaskCompletionSource[] joined =
    [
        new(TaskCreationOptions.RunContinuationsAsynchronously),
        new(TaskCreationOptions.RunContinuationsAsynchronously)
    ];
    private int admitted;

    internal Task<ControlledPartitionMovementNativeRetireCaptured> Arm()
    {
        lock (gate)
        {
            if (next is not null || admitted >= MaximumBorrows)
            { throw new InvalidOperationException("The finite original Retire borrow is already armed or exhausted."); }
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return next.Task;
        }
    }

    internal IGrainPartitionMovementSealedOperationObserver ForOwner(int index) => new Owner(this, index);

    internal Task JoinAsync(int ordinal, CancellationToken cancellationToken)
    {
        if ((uint)ordinal >= MaximumBorrows)
        { throw new ArgumentOutOfRangeException(nameof(ordinal)); }
        return joined[ordinal].Task.WaitAsync(cancellationToken);
    }

    internal Task JoinAdmittedAsync(CancellationToken cancellationToken)
    {
        int count;
        lock (gate)
        { count = admitted; }
        return Task.WhenAll(joined.Take(count).Select(completion => completion.Task)).WaitAsync(cancellationToken);
    }

    private async ValueTask AcceptAsync(int index, ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        if (phase.Stage != PartitionMovePeerStage.Retire || phase.ReceiverEffectAdmission is null)
        { throw new InvalidOperationException("The actual receiver did not seal original Retire authority."); }
        TaskCompletionSource<ControlledPartitionMovementNativeRetireCaptured>? selected;
        int ordinal;
        lock (gate)
        {
            selected = next;
            if (selected is null)
            { return; }
            next = null;
            ordinal = admitted++;
        }
        if ((uint)ordinal >= MaximumBorrows)
        { throw new InvalidOperationException("The original Retire borrow exceeded its admitted operation count."); }
        try
        {
            selected.SetResult(new(index, operation));
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        finally
        { joined[ordinal].TrySetResult(); }
    }

    private sealed class Owner(ControlledPartitionMovementNativeRetireBorrow owner, int index)
        : IGrainPartitionMovementSealedOperationObserver
    {
        public ValueTask BorrowAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
            => owner.AcceptAsync(index, operation, cancellationToken);
    }
}
