using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Authorization;

internal static class InitialPrincipalEpochClusterFlow
{
    internal static async Task RunAsync(long epoch)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                InitialPrincipalEpochClusterState? state = null;
                InitialPrincipalEpochClusterHealthyState? healthy = null;
                using (var deadline = McpCallerDeadline.Create())
                {
                    await RootAsync(fixture, async root =>
                    {
                        state = await InitialPrincipalEpochClusterSetup.RunAsync(root, epoch, deadline.Token);
                        await InitialPrincipalEpochClusterRefusals.InitialAsync(root, state, 1L, deadline.Token);
                        await InitialPrincipalEpochClusterValues.ObserveAsync(root, state, 1L,
                            () => InitialPrincipalEpochClusterRefusals.MissingAsync(root, state, deadline.Token), deadline.Token);
                        await InitialPrincipalEpochClusterLifecycle.RunAsync(fixture, root, state, deadline.Token);
                        healthy = await InitialPrincipalEpochClusterHealthy.CreateAsync(fixture, state, deadline.Token);
                        await InitialPrincipalEpochClusterHealthy.WaveAsync(fixture, root, state, healthy, deadline.Token);
                    }, deadline.Token);
                }
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await RootAsync(fixture, root => InitialPrincipalEpochClusterHealthy.WaveAsync(fixture, root,
                            state!, healthy!, deadline.Token), deadline.Token);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RootAsync(ClusterFixture fixture, Func<RequestCqrsClusterCallers, Task> action, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await action(root);
            return true;
        }, token);
}
