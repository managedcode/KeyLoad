using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochRf3Flow
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
                InitialPrincipalEpochRf3State? state = null;
                InitialPrincipalEpochRf3HealthyState? healthy = null;
                using (var deadline = McpCallerDeadline.Create())
                {
                    await RootAsync(fixture, async root =>
                    {
                        state = await InitialPrincipalEpochRf3Setup.RunAsync(root, epoch, deadline.Token);
                        await InitialPrincipalEpochRf3Refusals.InitialAsync(root, state, 1L, deadline.Token);
                        await InitialPrincipalEpochRf3Values.ObserveAsync(root, state, 1L,
                            () => InitialPrincipalEpochRf3Refusals.MissingAsync(root, state, deadline.Token), deadline.Token);
                        await InitialPrincipalEpochRf3Lifecycle.RunAsync(fixture, root, state, deadline.Token);
                        healthy = await InitialPrincipalEpochRf3Healthy.CreateAsync(fixture, root, state, deadline.Token);
                        await InitialPrincipalEpochRf3Healthy.WaveAsync(fixture, root, state, healthy, deadline.Token);
                    }, deadline.Token);
                }
                for (var cold = 0; cold < 2; cold++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlRf3Cold.RunAsync(fixture, deadline.Token);
                        await RootAsync(fixture, root => InitialPrincipalEpochRf3Healthy.WaveAsync(fixture, root,
                            state!, healthy!, deadline.Token), deadline.Token);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task RootAsync(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task> action, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await action(root);
            return true;
        }, token);
}
