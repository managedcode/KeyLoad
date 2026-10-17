using KeyLoad.Core;

namespace KeyLoad.Replication;

/// <summary>Coalesces published canonical cuts under the materializer's existing waiter-registration lock.</summary>
internal sealed class ReplicaMaterializerAppliedPositionSignals
{
    private readonly Lock signals;
    private readonly DatabaseEngine database;
    private readonly Action check;
    private TaskCompletionSource changed = NewSignal();
    private long publishedAppliedPosition;

    internal ReplicaMaterializerAppliedPositionSignals(Lock signals, DatabaseEngine database, Action check)
    {
        this.signals = signals ?? throw new ArgumentNullException(nameof(signals));
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(check);
        this.database = database;
        this.check = check;
    }

    internal async Task WaitForApplyAsync(long index, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        while (true)
        {
            Task signal;
            lock (signals)
            {
                check();
                if (database.LastApplied >= index)
                {
                    return;
                }
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<long> WaitForChangeAsync(long observedPosition, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task signal;
            lock (signals)
            {
                check();
                if (publishedAppliedPosition > observedPosition)
                {
                    return publishedAppliedPosition;
                }
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal void PublishCompletedCut(long completedCut)
    {
        lock (signals)
        {
            if (completedCut <= publishedAppliedPosition)
            {
                return;
            }
            publishedAppliedPosition = completedCut;
            RotateSignal();
        }
    }

    internal void WakeWaiters()
    {
        lock (signals)
        {
            RotateSignal();
        }
    }

    private void RotateSignal()
    {
        var previous = changed;
        changed = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
