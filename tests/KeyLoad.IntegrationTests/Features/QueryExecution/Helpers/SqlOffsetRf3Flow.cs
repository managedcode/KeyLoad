using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOffsetRf3Flow
{
    private const long DeniedEpoch = 3;
    private const long RepairedEpoch = 4;
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, callers => SqlOffsetRf3Setup.RunAsync(fixture, callers, token), token);
        await ReaderAsync(fixture, state, async callers =>
        {
            await SqlOffsetRf3Pages.HealthyAsync(callers, state, token);
            await SqlOffsetRf3Refusals.InvalidAsync(callers, state, token);
            await SqlOffsetRf3Pages.HealthyAsync(callers, state, token);
        }, token);
        await AdminAsync(fixture, state, callers => SqlOffsetRf3Setup.PolicyAsync(callers, state, DeniedEpoch, allow: false, token), token);
        await ReaderAsync(fixture, state, callers => SqlOffsetRf3Refusals.DeniedAsync(callers, state, token), token);
        await AdminAsync(fixture, state, callers => SqlOffsetRf3Setup.PolicyAsync(callers, state, RepairedEpoch, allow: true, token), token);
        await ReaderAsync(fixture, state, callers => SqlOffsetRf3Pages.HealthyAsync(callers, state, token), token);
        for (var cold = 0; cold < SqlOffsetRf3Protocol.ColdCuts; cold++)
        {
            await ModelSqlRf3Cold.RunAsync(fixture, token);
            await ReaderAsync(fixture, state, callers => SqlOffsetRf3Pages.HealthyAsync(callers, state, token), token);
        }
    }
    private static async Task ReaderAsync(ClusterFixture fixture, SqlOffsetRf3State state,
        Func<RequestCqrsRf3Callers, Task> use, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async callers => { await use(callers); return true; }, token);
        await AdminAsync(fixture, state, _ => Task.CompletedTask, token);
    }
    private static async Task AdminAsync(ClusterFixture fixture, SqlOffsetRf3State state,
        Func<RequestCqrsRf3Callers, Task> use, CancellationToken token)
        => _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async callers =>
            {
                await use(callers);
                await SqlOffsetRf3Sources.UnchangedAsync(callers, state, token);
                return true;
            }, token);
}
