using KeyLoad.RecoveryHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativePreStartDisposeFlow
{
    private const int FirstCold = 0;
    private const int ColdCount = 2;
    private const int NoFailures = 0;

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        ControlledPartitionMovementNativeHostEndpoints? endpoints = null;
        ControlledPartitionMovementNativeHosts? owners = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            endpoints = new();
            var settings = new ControlledPartitionMovementNativeHostSettings(endpoints);
            var localhost = new NativeLocalhostClusteringOptions(endpoints.Primary);
            await PartitionStorageNativePreStartDisposeTrial.RunAsync(settings, localhost, token)
                .ConfigureAwait(false);
            endpoints.Dispose();
            owners = new(settings, localhost);
            await owners.CreateNodesAsync(token).ConfigureAwait(false);
            await PartitionStorageNativeStartupWaitAssertions.StartAsync(owners, token).ConfigureAwait(false);
            var receipt = await PartitionStorageNativeHealthy.ExecuteAsync(owners.Source, token).ConfigureAwait(false);
            await PartitionStorageNativeHealthy.ReplayAsync(owners.Source, receipt, token).ConfigureAwait(false);
            for (var cold = FirstCold; cold < ColdCount; cold++)
            {
                await owners.RestartAsync(token).ConfigureAwait(false);
                await PartitionStorageNativeHealthy.ReplayAsync(owners.Source, receipt, token).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (owners is not null)
        {
            if (failures.Count != NoFailures)
            {
                owners.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => owners.DisposeAsync().AsTask(), failures)
                .ConfigureAwait(false);
        }
        if (endpoints is not null)
        {
            ServerFailureObserver.Observe(endpoints.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
