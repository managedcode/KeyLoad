using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOrdinalRf3Owned
{
    internal static Task WaveAsync(ClusterFixture fixture, SqlOffsetRf3State state,
        bool healthy, CancellationToken token)
        => RunAsync(fixture, state, async (callers, wire) =>
        {
            if (healthy)
            { await SqlOrdinalRf3Pages.HealthyAsync(callers, state, wire, token); }
            else
            {
                await SqlOrdinalRf3Controls.InvalidAsync(fixture, callers, state, wire, token);
                await SqlOrdinalRf3Controls.CancelledAsync(fixture, callers, state, wire, token);
                await SqlOrdinalRf3Pages.HealthyAsync(callers, state, wire, token);
            }
        }, token);
    internal static Task DeniedAsync(ClusterFixture fixture, SqlOffsetRf3State state,
        CancellationToken token)
        => RunAsync(fixture, state, (callers, wire) =>
            SqlOrdinalRf3Controls.DeniedAsync(callers, state, wire, token), token);
    private static async Task RunAsync(ClusterFixture fixture, SqlOffsetRf3State state,
        Func<RequestCqrsRf3Callers, PostgresRf3Caller, Task> use, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresRf3Caller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresRf3Certificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => use(caller, wire), failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await SourcesAsync(fixture, state, token);
    }
    internal static async Task SourcesAsync(ClusterFixture fixture, SqlOffsetRf3State state, CancellationToken token)
        => _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            { await SqlOffsetRf3Sources.UnchangedAsync(caller, state, token); return true; }, token);
}
