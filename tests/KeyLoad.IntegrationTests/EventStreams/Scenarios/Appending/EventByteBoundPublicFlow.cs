using KeyLoad.IntegrationTests.ChangeFeeds;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventByteBoundPublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async admin =>
            {
                var seeded = await EventByteBoundPublicSeed.CreateAsync(fixture, admin, token);
                await WithReaderAsync(fixture, admin, seeded, true, token);
                return seeded;
            }, token);
        for (var cold = 0; cold < 2; cold++)
        {
            await FeedLiveClusterCold.RestartAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async admin =>
                {
                    await WithReaderAsync(fixture, admin, state, false, token);
                    return true;
                }, token);
        }
    }

    private static Task<bool> WithReaderAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin,
        EventByteBoundPublicState state, bool capture, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret,
            async reader =>
            {
                await EventByteBoundPublicCurrent.ObserveAsync(admin, state, token);
                foreach (var direction in new[] { StreamReadDirection.Forward, StreamReadDirection.Backward })
                {
                    await EventByteBoundPublicRefusals.RefuseAsync(admin, reader, state,
                        EventByteBoundPublicExpected.First(state, direction, 1), token);
                    var indices = direction == StreamReadDirection.Forward ? new[] { 0 } : [2];
                    var first = await EventByteBoundPublicCalls.ReadAsync(admin, reader, state,
                        EventByteBoundPublicExpected.First(state, direction, 0), indices, true, null, token);
                    if (capture)
                    {
                        state.Captures.Add((direction, first.Sdk));
                        state.Captures.Add((direction, first.Mcp));
                    }
                }
                foreach (var captured in state.Captures)
                {
                    await EventByteBoundPublicRefusals.RefuseAsync(admin, reader, state,
                        EventByteBoundPublicExpected.Resume(state, captured.Direction, captured.Page, 1), token);
                    var indices = captured.Direction == StreamReadDirection.Forward ? new[] { 1, 2 } : [1, 0];
                    await EventByteBoundPublicCalls.ReadAsync(admin, reader, state,
                        EventByteBoundPublicExpected.Resume(state, captured.Direction, captured.Page, 0),
                        indices, false, captured.Page.SnapshotCutPosition, token);
                }
                await EventByteBoundPublicCalls.ReplayAsync(admin, state, token);
                return true;
            }, token);
}
