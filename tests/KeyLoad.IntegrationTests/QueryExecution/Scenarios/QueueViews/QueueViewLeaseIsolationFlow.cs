using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            ClusterFixtureFailureRootRetention.For(fixture).Enable();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                QueueViewLeaseIsolationState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => QueueViewLeaseIsolationSeed.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                using (var deadline = McpCallerDeadline.Create())
                {
                    await QueueViewLeaseIsolationWarm.RunAsync(fixture, state, deadline.Token);
                }
                await QueueViewLeaseIsolationCold.RunAsync(fixture, state);
            }, failures);
            if (failures.Count > QueueViewLeaseIsolationProtocol.Empty)
            {
                ClusterFixtureFailureRootRetention.For(fixture).Retain();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
