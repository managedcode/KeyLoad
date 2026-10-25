using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.ResourceExecution;

/// <summary>Holds the store gate as a reader, or as a writer inside the exclusive apply stage of a real commit.
/// A write hold needs <see cref="StageObserver"/> registered as the store's fault observer before <see cref="Start"/>.</summary>
internal sealed class ZoneTreePointCacheOwnerGateHold
{
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(10);
    private static readonly byte[] HoldKey = "cache/owner/gate-hold"u8.ToArray();
    private static readonly byte[] HoldValue = [1];
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<bool>? holder;
    private int armed;

    internal Action<CommitStage, long, int> StageObserver => (stage, _, _) => ObserveStage(stage);

    private void ObserveStage(CommitStage stage)
    {
        if (stage != CommitStage.MutationApplied || Interlocked.Exchange(ref armed, 0) == 0)
        {
            return;
        }
        Hold();
    }

    internal void Start(ZoneTreeStore store, bool write)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (holder is not null)
        {
            throw new InvalidOperationException("The gate hold has already started.");
        }
        if (write)
        {
            Volatile.Write(ref armed, 1);
            holder = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() => store.Commit((transaction, _) =>
            {
                transaction.Put(HoldKey, HoldValue);
                return true;
            }));
            return;
        }
        holder = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() => store.Read(_ => Hold()));
    }

    internal Task WaitUntilEnteredAsync() => entered.Task.WaitAsync(HoldTimeout, TimeProvider.System);

    internal void Release() => release.TrySetResult();

    internal Task JoinHolderAsync(List<Exception> failures)
        => holder is null ? Task.CompletedTask : ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(holder, failures);

    private bool Hold()
    {
        entered.TrySetResult();
        release.Task.WaitAsync(HoldTimeout, TimeProvider.System).GetAwaiter().GetResult();
        return true;
    }
}
