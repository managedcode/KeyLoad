using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheOwnerGateHold
{
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(10);
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task holder;

    internal ZoneTreePointCacheOwnerGateHold(ZoneTreeStore store, bool write)
    {
        ArgumentNullException.ThrowIfNull(store);
        holder = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() =>
            write ? store.Commit((_, _) => Hold()) : store.Read(_ => Hold()));
    }

    internal Task WaitUntilEnteredAsync() => entered.Task.WaitAsync(HoldTimeout, TimeProvider.System);

    internal void Release() => release.TrySetResult();

    internal Task JoinHolderAsync(List<Exception> failures)
        => ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(holder, failures);

    private bool Hold()
    {
        entered.TrySetResult();
        release.Task.WaitAsync(HoldTimeout, TimeProvider.System).GetAwaiter().GetResult();
        return true;
    }
}
