using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.ResourceExecution;

/// <summary>Holds the exclusive store gate inside a real commit's apply stage, so every reader waits at the gate.
/// Pass <see cref="Observer"/> as the store's fault observer before calling <see cref="Start"/>.</summary>
internal sealed class RealZoneTreeWriteGateHold : IAsyncDisposable
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);
    private static readonly byte[] HoldKey = "execution/gate-hold"u8.ToArray();
    private static readonly byte[] HoldValue = [1];
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? holder;
    private int armed;

    /// <summary>The store fault observer that blocks the held commit while it applies, the only stage that excludes readers.</summary>
    internal Action<CommitStage, long, int> Observer => (stage, _, _) => Observe(stage);

    private void Observe(CommitStage stage)
    {
        if (stage != CommitStage.MutationApplied || Interlocked.Exchange(ref armed, 0) == 0)
        {
            return;
        }
        entered.TrySetResult();
        release.Task.WaitAsync(CoordinationTimeout, TimeProvider.System).GetAwaiter().GetResult();
    }

    /// <summary>Starts a real one-mutation commit whose apply stage waits for explicit release.</summary>
    internal void Start(ZoneTreeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (holder is not null)
        {
            throw new InvalidOperationException("The gate hold has already started.");
        }
        Volatile.Write(ref armed, 1);
        holder = Task.Factory.StartNew(() => store.Commit((transaction, _) =>
        {
            transaction.Put(HoldKey, HoldValue);
            return true;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>Waits until the commit holds the exclusive store gate.</summary>
    internal Task WaitUntilEnteredAsync() => entered.Task.WaitAsync(CoordinationTimeout, TimeProvider.System);

    /// <summary>Releases the apply stage and waits for the real commit to exit.</summary>
    internal async Task ReleaseAsync()
    {
        release.TrySetResult();
        if (holder is null)
        {
            return;
        }
        try
        {
            await holder.WaitAsync(CoordinationTimeout, TimeProvider.System);
        }
        finally
        {
            await holder;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await ReleaseAsync();
}
