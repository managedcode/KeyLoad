using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextAuthorizedQueryR39Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async admin =>
            {
                var original = await NativeTextAuthorizedQueryR39Seed.CreateAsync(fixture, admin, token);
                await SessionAsync(fixture, admin, original, 1L, token);
                foreach (var epoch in new[] { 2L, 3L, 4L })
                {
                    await NativeTextAuthorizedQueryR39Policy.ApplyAsync(admin, original, epoch, token);
                    await SessionAsync(fixture, admin, original, epoch, token);
                }
                return original;
            }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async admin =>
            {
                await SessionAsync(fixture, admin, state, 4L, token);
                await NativeTextAuthorizedQueryR39Policy.ApplyAsync(admin, state, 5L, token);
                await SessionAsync(fixture, admin, state, 5L, token);
                return true;
            }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async admin =>
            { await SessionAsync(fixture, admin, state, 5L, token); return true; }, token);
    }

    private static Task<bool> SessionAsync(ClusterFixture fixture, RequestCqrsRf3Callers admin,
        NativeTextAuthorizedQueryR39State state, long epoch, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Identity.Secret,
            async reader =>
            {
                var baseline = await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.OutboxStatusAsync(state.Original.Scenario.Partition, token));
                async Task CurrentAsync()
                {
                    if (epoch == 4L)
                    { await NativeTextAuthorizedQueryR39Replay.DeniedAsync(reader, admin, state, baseline, token); }
                    else
                    { await NativeTextAuthorizedQueryR39Pages.ReaderAsync(reader, state, epoch, admin, baseline, token); }
                    await NativeTextAuthorizedQueryR39Pages.AdminAsync(admin, state, baseline, token);
                }
                await NativeTextAuthorizedQueryR39Replay.RunAsync(admin, state, CurrentAsync, token);
                return true;
            }, token);
}
