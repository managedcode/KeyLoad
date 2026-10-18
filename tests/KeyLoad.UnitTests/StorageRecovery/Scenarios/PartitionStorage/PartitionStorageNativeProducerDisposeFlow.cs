using KeyLoad.RecoveryHost.Features.ClusterRouting;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProducerDisposeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeProducerDisposeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        ControlledPartitionMovementNativeHostEndpoints? endpoints = null;
        ControlledPartitionMovementNativeHosts? owners = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            endpoints = new();
            var settings = new ControlledPartitionMovementNativeHostSettings(endpoints);
            var localhost = new NativeLocalhostClusteringOptions(endpoints.Primary);
            var original = await PartitionStorageNativeProducerDisposeTrial.RunAsync(settings, localhost, endpoints, token)
                .ConfigureAwait(false);
            owners = new(settings, localhost);
            await owners.CreateNodesAsync(token).ConfigureAwait(false);
            var starting = Task.WhenAll(owners.Nodes.Select(node => node.StartAsync(token)));
            await ControlledPartitionMovementNativeHosts.JoinOriginalAsync(starting).ConfigureAwait(false);
            await PartitionStorageNativeProducerDisposeAssertions.IdentityAsync(original.Identity, owners.Source.Store.Identity);
            var receipt = await PartitionStorageNativeHealthy.ExecuteAsync(owners.Source, token).ConfigureAwait(false);
            await PartitionStorageNativeHealthy.ReplayAsync(owners.Source, receipt, token).ConfigureAwait(false);
            for (var cold = P.FirstCold; cold < P.ColdCount; cold++)
            {
                await owners.RestartAsync(token).ConfigureAwait(false);
                await PartitionStorageNativeProducerDisposeAssertions.IdentityAsync(original.Identity, owners.Source.Store.Identity);
                await PartitionStorageNativeHealthy.ReplayAsync(owners.Source, receipt, token).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (owners is not null)
        {
            if (failures.Count != P.NoFailures)
            {
                owners.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => owners.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (endpoints is not null)
        {
            ServerFailureObserver.Observe(endpoints.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
