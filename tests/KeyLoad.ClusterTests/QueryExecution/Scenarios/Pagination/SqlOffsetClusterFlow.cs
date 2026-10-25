using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class SqlOffsetClusterFlow
{
    private const long DeniedEpoch = 3;
    private const long RepairedEpoch = 4;
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, callers => SqlOffsetClusterSetup.RunAsync(fixture, callers, token), token);
        await ReaderAsync(fixture, state, async callers =>
        {
            await SqlOffsetClusterPages.HealthyAsync(callers, state, token);
            await SqlOffsetClusterRefusals.InvalidAsync(callers, state, token);
            await SqlOffsetClusterPages.HealthyAsync(callers, state, token);
        }, token);
        await AdminAsync(fixture, state, callers => SqlOffsetClusterSetup.PolicyAsync(callers, state, DeniedEpoch, allow: false, token), token);
        await ReaderAsync(fixture, state, callers => SqlOffsetClusterRefusals.DeniedAsync(callers, state, token), token);
        await AdminAsync(fixture, state, callers => SqlOffsetClusterSetup.PolicyAsync(callers, state, RepairedEpoch, allow: true, token), token);
        await ReaderAsync(fixture, state, callers => SqlOffsetClusterPages.HealthyAsync(callers, state, token), token);
        for (var cold = 0; cold < SqlOffsetClusterProtocol.ColdCuts; cold++)
        {
            await ModelSqlClusterCold.RunAsync(fixture, token);
            await ReaderAsync(fixture, state, callers => SqlOffsetClusterPages.HealthyAsync(callers, state, token), token);
        }
    }
    private static async Task ReaderAsync(ClusterFixture fixture, SqlOffsetClusterState state,
        Func<RequestCqrsClusterCallers, Task> use, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async callers => { await use(callers); return true; }, token);
        await AdminAsync(fixture, state, _ => Task.CompletedTask, token);
    }
    private static async Task AdminAsync(ClusterFixture fixture, SqlOffsetClusterState state,
        Func<RequestCqrsClusterCallers, Task> use, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async callers =>
            {
                await use(callers);
                await SqlOffsetClusterSources.UnchangedAsync(callers, state, token);
                return true;
            }, token);
}
