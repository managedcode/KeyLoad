using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRawReadR43Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = new GraphPathRawReadR43State(await GraphPathRf3Scenario.CreateAsync(fixture, token));
        await Assert.That(state.Identity.Principal.PolicyEpoch).IsEqualTo(1L);
        await OwnedAsync(fixture, state, async (admin, reader) =>
        {
            await GraphPathRawReadR43Commands.SeedAsync(admin, state, token);
            await PolicyAsync(admin, reader, state, false, 2L, token);
            await PolicyAsync(admin, reader, state, true, 3L, token);
            await PolicyAsync(admin, reader, state, false, 4L, token);
        }, token);
        await ColdAsync(fixture, state, token);
        await OwnedAsync(fixture, state, (admin, reader) =>
            PolicyAsync(admin, reader, state, true, 5L, token), token);
        await ColdAsync(fixture, state, token);
    }

    private static async Task PolicyAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        GraphPathRawReadR43State state, bool allowed, long epoch, CancellationToken token)
    {
        await GraphPathRawReadR43Commands.PolicyAsync(admin, state, allowed, token);
        await Assert.That(state.Identity.Principal.PolicyEpoch).IsEqualTo(epoch);
        await GraphPathRawReadR43Replay.VerifyAsync(admin, reader, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, GraphPathRawReadR43State state,
        CancellationToken token)
    {
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        await OwnedAsync(fixture, state, (admin, reader) =>
            GraphPathRawReadR43Replay.VerifyAsync(admin, reader, state, token), token);
    }

    private static async Task OwnedAsync(ClusterFixture fixture, GraphPathRawReadR43State state,
        Func<RequestCqrsRf3Callers, RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Identity.Secret, async reader => { await operation(admin, reader); return true; }, token), token);
}
