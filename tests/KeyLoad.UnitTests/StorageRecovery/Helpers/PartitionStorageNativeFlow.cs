using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeFlow
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
            var partial = await PartitionStorageNativePartialTrial.RunAsync(settings, localhost, endpoints, token)
                .ConfigureAwait(false);
            owners = new(settings, localhost);
            await owners.CreateNodesAsync(token).ConfigureAwait(false);
            var starting = Task.WhenAll(owners.Nodes.Select(node => node.StartAsync(token)));
            await ControlledPartitionMovementNativeHosts.JoinOriginalAsync(starting).ConfigureAwait(false);
            await RequireIdentityAsync(partial, owners.Source).ConfigureAwait(false);
            var receipt = await PartitionStorageNativeHealthy.ExecuteAsync(owners.Source, token).ConfigureAwait(false);
            await PartitionStorageNativeHealthy.ReplayAsync(owners.Source, receipt, token).ConfigureAwait(false);
            for (var cold = FirstCold; cold < ColdCount; cold++)
            {
                await owners.RestartAsync(token).ConfigureAwait(false);
                await RequireIdentityAsync(partial, owners.Source).ConfigureAwait(false);
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

    private static async Task RequireIdentityAsync(PartitionStorageNativePartialStart original,
        ControlledPartitionMovementNativeHost source)
    {
        var actual = source.Store.Read(_ => source.Store.Identity);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(original.Identity)))
            .IsTrue();
    }
}
