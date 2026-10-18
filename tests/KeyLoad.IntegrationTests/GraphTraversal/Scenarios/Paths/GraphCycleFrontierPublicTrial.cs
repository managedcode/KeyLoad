using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphCycleFrontierPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = new GraphCycleFrontierPublicState(await GraphIncomingClusterScenario.CreateAsync(fixture, token));
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphCycleFrontierPublicCommands.SeedAsync(admin, state, token);
            await VerifyAsync(admin, reader, state, token);
            await GraphCycleFrontierPublicCommands.VisibilityAsync(admin, state, true, token);
            await VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphCycleFrontierPublicCommands.VisibilityAsync(admin, state, false, token);
            await VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
    }

    private static async Task VerifyAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
        GraphCycleFrontierPublicState state, CancellationToken token)
    {
        await GraphCycleFrontierPublicBounds.VerifyAsync(admin, reader, state, token);
        await GraphCycleFrontierPublicCurrent.ReplayAsync(admin, reader, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, GraphCycleFrontierPublicState state, CancellationToken token)
    {
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        await OwnedAsync(fixture, state, (admin, reader) => VerifyAsync(admin, reader, state, token), token);
    }

    private static async Task OwnedAsync(ClusterFixture fixture, GraphCycleFrontierPublicState state,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Seed.Reader.Secret, async reader => { await operation(admin, reader); return true; }, token), token);
}
