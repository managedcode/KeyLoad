using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOrdinalClusterOwned
{
    internal static Task WaveAsync(ClusterFixture fixture, SqlOffsetClusterState state,
        bool healthy, CancellationToken token)
        => RunAsync(fixture, state, async (callers, wire) =>
        {
            if (healthy)
            {
                await SqlOrdinalClusterPages.HealthyAsync(callers, state, wire, token);
            }
            else
            {
                await SqlOrdinalClusterControls.InvalidAsync(fixture, callers, state, wire, token);
                await SqlOrdinalClusterControls.CancelledAsync(fixture, callers, state, wire, token);
                await SqlOrdinalClusterPages.HealthyAsync(callers, state, wire, token);
            }
        }, token);
    internal static Task DeniedAsync(ClusterFixture fixture, SqlOffsetClusterState state,
        CancellationToken token)
        => RunAsync(fixture, state, (callers, wire) =>
            SqlOrdinalClusterControls.DeniedAsync(callers, state, wire, token), token);
    private static async Task RunAsync(ClusterFixture fixture, SqlOffsetClusterState state,
        Func<RequestCqrsClusterCallers, PostgresClusterCaller, Task> use, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async caller =>
        {
            var failures = new List<Exception>();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var wire = await PostgresClusterCaller.ConnectAsync(fixture.App, McpCallerProtocol.Node3,
                    state.Identity.Principal.Id, state.Identity.Secret, PostgresClusterCertificate.RootPath(fixture), token);
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => use(caller, wire), failures);
            }, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            return true;
        }, token);
        await SourcesAsync(fixture, state, token);
    }
    internal static async Task SourcesAsync(ClusterFixture fixture, SqlOffsetClusterState state, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller =>
            {
                await SqlOffsetClusterSources.UnchangedAsync(caller, state, token);
                return true;
            }, token);
}
