using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOrdinalRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture)
    {
        var state = await DeadlineAsync(token => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App,
            McpCallerProtocol.Node1, fixture.AdminKey, caller => SqlOffsetRf3Setup.RunAsync(fixture,
                caller, PostgresRf3State.Partition, token), token));
        await StageAsync(token => SqlOrdinalRf3Owned.WaveAsync(fixture, state, healthy: true, token));
        await StageAsync(token => SqlOrdinalRf3Owned.WaveAsync(fixture, state, healthy: false, token));
        await StageAsync(token => PolicyAsync(fixture, state, DeniedEpoch, false, token));
        await StageAsync(token => SqlOrdinalRf3Owned.DeniedAsync(fixture, state, token));
        await StageAsync(token => PolicyAsync(fixture, state, RepairedEpoch, true, token));
        await StageAsync(token => SqlOrdinalRf3Owned.WaveAsync(fixture, state, healthy: true, token));
        for (var cold = 0; cold < SqlOffsetRf3Protocol.ColdCuts; cold++)
        {
            await StageAsync(token => ModelSqlRf3Cold.RunAsync(fixture, token));
            await StageAsync(token => SqlOrdinalRf3Owned.WaveAsync(fixture, state, healthy: true, token));
        }
    }
    private static async Task PolicyAsync(ClusterFixture fixture, SqlOffsetRf3State state,
        long epoch, bool allow, CancellationToken token)
        => _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                await SqlOffsetRf3Setup.PolicyAsync(callers, state, epoch, allow, token);
                await SqlOffsetRf3Sources.UnchangedAsync(callers, state, token);
                return true;
            }, token);
    private static async Task StageAsync(Func<CancellationToken, Task> use)
        => _ = await DeadlineAsync(async token => { await use(token); return true; });
    private static async Task<T> DeadlineAsync<T>(Func<CancellationToken, Task<T>> use)
    {
        T result = default!;
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = McpCallerDeadline.Create();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                { result = await use(deadline.Token); }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }
    private const long DeniedEpoch = 3, RepairedEpoch = 4;
}
