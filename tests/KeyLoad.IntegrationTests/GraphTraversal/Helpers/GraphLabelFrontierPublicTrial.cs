using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphLabelFrontierPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = new GraphLabelFrontierPublicState(await GraphIncomingRf3Scenario.CreateAsync(fixture, token));
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphLabelFrontierPublicCommands.SeedAsync(admin, state, token);
            await VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphLabelFrontierPublicCommands.ReplaceAsync(admin, state, token);
            await VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
    }

    private static async Task VerifyAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        GraphLabelFrontierPublicState state, CancellationToken token)
    {
        await GraphLabelFrontierPublicBounds.VerifyAsync(admin, reader, state, token);
        await GraphLabelFrontierPublicCurrent.ReplayAsync(admin, reader, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, GraphLabelFrontierPublicState state, CancellationToken token)
    {
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        await OwnedAsync(fixture, state, (admin, reader) => VerifyAsync(admin, reader, state, token), token);
    }

    private static async Task OwnedAsync(ClusterFixture fixture, GraphLabelFrontierPublicState state,
        Func<RequestCqrsRf3Callers, RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Seed.Reader.Secret, async reader => { await operation(admin, reader); return true; }, token), token);
}
