using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class EventingRequiredClaimClusterFlow
{
    internal static async Task RunAsync(bool header)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                EventingRequiredClaimClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        async root => new EventingRequiredClaimClusterState(await RequiredProcessingPolicyClusterSetup.RunAsync(root, header, deadline.Token)), deadline.Token);
                }
                await WaveAsync(fixture, state, true);
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, EventingRequiredClaimClusterState state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Input.Key, async reader =>
            {
                if (warm)
                {
                    await EventingRequiredClaimClusterWarm.RunAsync(root, reader, state, deadline.Token);
                }
                else
                {
                    await EventingRequiredClaimClusterFinal.RunAsync(root, reader, state, deadline.Token);
                }
                return true;
            }, deadline.Token), deadline.Token);
    }
}
