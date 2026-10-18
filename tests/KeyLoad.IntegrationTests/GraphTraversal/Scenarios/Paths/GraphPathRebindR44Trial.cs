using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRebindR44Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var seed = await GraphPathClusterScenario.CreateAsync(fixture, token);
        var identity = await GraphPathClusterScenario.GrantLabelUseAsync(fixture, seed, token);
        var state = new GraphPathRebindR44State(seed, identity);
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphPathRebindR44Commands.SeedAsync(admin, state, token);
            await GraphPathRebindR44Replay.VerifyAsync(admin, reader, state, token);
            await GraphPathRebindR44Commands.ReplaceAsync(admin, state, 2, token);
            state.Stale = new(Guid.NewGuid(), state.Seed.Partition,
                [new UpsertEdge(GraphPathClusterScenario.Graph, "0-sa", state.S, state.A,
                    GraphPathClusterScenario.Label, "{}", 1L)]);
            await GraphPathRebindR44Replay.VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphPathRebindR44Commands.ReplaceAsync(admin, state, 3, token);
            await GraphPathRebindR44Replay.VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, GraphPathRebindR44State state, CancellationToken token)
    {
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        await OwnedAsync(fixture, state, (admin, reader) => GraphPathRebindR44Replay.VerifyAsync(admin, reader, state, token), token);
    }

    private static async Task OwnedAsync(ClusterFixture fixture, GraphPathRebindR44State state,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Identity.Secret, async reader => { await operation(admin, reader); return true; }, token), token);
}
