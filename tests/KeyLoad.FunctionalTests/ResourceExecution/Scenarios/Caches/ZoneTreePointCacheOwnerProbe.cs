using KeyLoad.Storage.ZoneTree.ResourceExecution;

namespace KeyLoad.FunctionalTests.ResourceExecution;

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
        var result = await current.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System);
        operation = null;
        return result;
    }

    internal Task JoinAsync(List<Exception> failures)
        => operation is { } current
            ? ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(current, failures)
            : Task.CompletedTask;
}
