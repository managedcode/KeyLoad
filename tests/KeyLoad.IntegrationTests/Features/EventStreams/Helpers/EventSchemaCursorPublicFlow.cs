using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSchemaCursorPublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin =>
            {
                var seeded = await EventSchemaCursorPublicSeed.CreateAsync(fixture, admin, token);
                await WithReaderAsync(fixture, admin, seeded, 0, token);
                return seeded;
            }, token);
        for (var cold = 1; cold <= 2; cold++)
        {
            await FeedLiveRf3Cold.RestartAsync(fixture, token);
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                async admin => { await WithReaderAsync(fixture, admin, state, cold, token); return true; }, token);
        }
    }

    private static Task<bool> WithReaderAsync(ClusterFixture fixture, RequestCqrsRf3Callers admin,
        EventSchemaCursorPublicState state, int stage, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Reader.Secret,
            async reader =>
            {
                await EventSchemaCursorPublicCalls.HealthyAsync(admin, reader, state, token);
                await EventSchemaCursorPublicPolicy.VerifyCurrentAsync(admin, reader, state, token);
                if (stage == 0)
                {
                    await EventSchemaCursorPublicCalls.CaptureAsync(admin, reader, state, token);
                    await EventSchemaCursorPublicCalls.ReplayAsync(admin, reader, state, token);
                    await EventSchemaCursorPublicPolicy.ChangeAsync(admin, reader, state, 2L, token);
                    await EventSchemaCursorPublicRefusals.OldAsync(admin, reader, state, token);
                    await EventSchemaCursorPublicCalls.CaptureAsync(admin, reader, state, token);
                }
                else
                {
                    await EventSchemaCursorPublicRefusals.OldAsync(admin, reader, state, token);
                    await EventSchemaCursorPublicCalls.ContinueAsync(admin, reader, state, token);
                    if (stage == 1)
                    {
                        await EventSchemaCursorPublicPolicy.ChangeAsync(admin, reader, state, 3L, token);
                        await EventSchemaCursorPublicRefusals.OldAsync(admin, reader, state, token);
                        await EventSchemaCursorPublicCalls.CaptureAsync(admin, reader, state, token);
                    }
                }
                await EventSchemaCursorPublicCalls.ReplayAsync(admin, reader, state, token);
                await EventSchemaCursorPublicPolicy.ReplayAsync(admin, reader, state, token);
                await EventSchemaCursorPublicCalls.HealthyAsync(admin, reader, state, token);
                return true;
            }, token);
}
