using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyClusterFlow
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
                RequiredProcessingPolicyClusterState? state = null;
                using (var deadline = McpCallerDeadline.Create())
                {
                    await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
                    {
                        state = await RequiredProcessingPolicyClusterSetup.RunAsync(root, header, deadline.Token);
                        return true;
                    }, deadline.Token);
                }
                await WaveAsync(fixture, state!, true);
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var deadline = McpCallerDeadline.Create();
                        await ModelSqlClusterCold.RunAsync(fixture, deadline.Token);
                        await WaveAsync(fixture, state!, false);
                    }, failures);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, RequiredProcessingPolicyClusterState state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Key, async reader =>
            {
                if (warm)
                {
                    await RequiredProcessingPolicyClusterWarm.RunAsync(root, reader, state, deadline.Token);
                }
                else
                {
                    await RequiredProcessingPolicyClusterFinal.RunAsync(root, reader, state, deadline.Token);
                }
                return true;
            }, deadline.Token);
            return true;
        }, deadline.Token);
    }
}
