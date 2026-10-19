namespace KeyLoad.Server;

internal static class PartitionHostDisposal
{
    internal static async Task RunAsync(PartitionHost host, Action<List<Exception>> disposeVisibility, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(() => host.OnlineTextMaintenance.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => host.TextMaintenance.DisposeAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => host.AnnMaintenance.DisposeAsync().AsTask(), failures);
        ServerFailureObserver.Observe(host.TextProjection.Dispose, failures);
        disposeVisibility(failures);
        ServerFailureObserver.Observe(host.CacheMemory.Dispose, failures);
    }

}
