using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

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

    internal Task WaitUntilEnteredAsync() => entered.Task.WaitAsync(HoldTimeout);

    internal void Release() => release.TrySetResult();

    internal Task JoinHolderAsync(List<Exception> failures)
        => ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(holder, failures);

    private bool Hold()
    {
        entered.TrySetResult();
        release.Task.WaitAsync(HoldTimeout).GetAwaiter().GetResult();
        return true;
    }
}

internal readonly record struct ZoneTreePointCacheOwnerObservation(
    ZoneTreePointCacheOwnerStatus Status, ZoneTreePointCacheOwnerIdentity Identity);

internal sealed class ZoneTreePointCacheOwnerProbe
{
    private const string MissingObservation = "No owner observation is registered.";
    private Task<ZoneTreePointCacheOwnerObservation>? operation;

    internal void Start(ZoneTreePointCacheControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        operation = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() =>
        {
            var status = control.TryReadOwnerIdentity(out var identity);
            return new ZoneTreePointCacheOwnerObservation(status, identity);
        });
    }

    internal async Task<ZoneTreePointCacheOwnerObservation> WaitAsync()
    {
        var current = operation ?? throw new InvalidOperationException(MissingObservation);
        var result = await current.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit);
        operation = null;
        return result;
    }

    internal Task JoinAsync(List<Exception> failures)
        => operation is { } current
            ? ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(current, failures)
            : Task.CompletedTask;
}
