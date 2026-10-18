namespace KeyLoad.Server;

internal static class PartitionHostDisposal
{
    internal static async Task RunAsync(PartitionHost host, Action closeStores, Action<List<Exception>> disposeVisibility)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => host.OnlineTextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.TextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => host.AnnMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(host.TextProjection.Dispose, failures);
        disposeVisibility(failures);
        ServerFailureObserver.Observe(host.CacheMemory.Dispose, failures);
        ServerFailureObserver.Observe(closeStores, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
