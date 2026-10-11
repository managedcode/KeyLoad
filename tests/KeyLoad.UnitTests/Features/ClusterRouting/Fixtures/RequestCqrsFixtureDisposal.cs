namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed partial class RequestCqrsClusterFixture
{
    private async Task DisposeOwnedResourcesAsync(List<Exception> failures)
    {
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Cluster.DisposeAsync().AsTask(), failures);
        if (requestWork is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures);
        }
        if (NativeMaintenance is not null)
        {
            if (!clusterStopped)
            { return; }
            var before = failures.Count;
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => NativeMaintenance.DisposeAsync().AsTask(), failures);
            if (failures.Count != before)
            { return; }
        }
        if (NativeAnnMaintenance is not null)
        {
            if (!clusterStopped)
            { return; }
            var before = failures.Count;
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => NativeAnnMaintenance.DisposeAsync().AsTask(), failures);
            if (failures.Count != before)
            { return; }
        }
        try
        {
            KeyLoad.Server.ServerFailureObserver.Observe(Database.Dispose, failures);
        }
        finally
        {
            RequestCqrsFixtureOwners.Release(ownerId);
        }
    }
}
