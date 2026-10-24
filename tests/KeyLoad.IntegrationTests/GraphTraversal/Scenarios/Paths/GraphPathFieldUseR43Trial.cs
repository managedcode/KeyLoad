using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.GraphTraversal;

internal static class GraphPathFieldUseR43Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = new GraphPathFieldUseR43State(await GraphPathClusterScenario.CreateAsync(fixture, token));
        await Assert.That(state.Identity.Principal.PolicyEpoch).IsEqualTo(1L);
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphPathFieldUseR43Commands.SeedAsync(admin, state, token);
            await GraphPathFieldUseR43Commands.PolicyAsync(admin, state, true, token);
            await Assert.That(state.Identity.Principal.PolicyEpoch).IsEqualTo(state.AllowedEpoch);
            await GraphPathFieldUseR43Replay.VerifyAsync(admin, reader, state, token);
            await GraphPathFieldUseR43Commands.PolicyAsync(admin, state, false, token);
            await Assert.That(state.Identity.Principal.PolicyEpoch).IsEqualTo(3L);
            await GraphPathFieldUseR43Replay.VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
        state.AllowedEpoch = 4L;
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphPathFieldUseR43Commands.PolicyAsync(admin, state, true, token);
            await Assert.That(state.Identity.Principal.PolicyEpoch).IsEqualTo(state.AllowedEpoch);
            await GraphPathFieldUseR43Replay.VerifyAsync(admin, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, GraphPathFieldUseR43State state,
        CancellationToken token)
    {
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        await OwnedAsync(fixture, state, (admin, reader) =>
            GraphPathFieldUseR43Replay.VerifyAsync(admin, reader, state, token), token);
    }

    private static async Task OwnedAsync(ClusterFixture fixture, GraphPathFieldUseR43State state,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Identity.Secret, async reader => { await operation(admin, reader); return true; }, token), token);
}
