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
        if (!clusterStopped || requestWork is { IsJoined: false })
        {
            return;
        }
        if (NativeMaintenance is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => NativeMaintenance.DisposeAsync().AsTask(), failures);
        }
        if (NativeAnnMaintenance is not null)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => NativeAnnMaintenance.DisposeAsync().AsTask(), failures);
        }
        if (failures.Count != 0)
        {
            return;
        }
        KeyLoad.Server.ServerFailureObserver.Observe(Database.Dispose, failures);
        if (failures.Count == 0)
        {
            RequestCqrsFixtureOwners.Release(ownerId);
        }
    }
}
