using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class LeftFilteredJoinPublicFlow
{
    internal static Task DeniedAsync(ClusterFixture fixture, LeftFilteredJoinState state, CancellationToken token)
        => WithReaderAsync(fixture, state, callers => LeftFilteredJoinRefusals.DeniedAsync(callers, state, token), token);

    internal static Task HealthyAsync(ClusterFixture fixture, LeftFilteredJoinState state,
        bool masked, CancellationToken token)
        => WithReaderAsync(fixture, state, async callers =>
        {
            await LeftFilteredJoinAssertions.PageAsync(callers, state, masked, token);
            await LeftFilteredJoinRefusals.InvalidAsync(callers, state, token);
            await LeftFilteredJoinAssertions.PageAsync(callers, state, masked, token);
        }, token);

    private static async Task WithReaderAsync(ClusterFixture fixture, LeftFilteredJoinState state,
        Func<RequestCqrsRf3Callers, Task> operation, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async callers => { await operation(callers); return true; }, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async callers => { await LeftFilteredJoinRefusals.SourcesAsync(callers.Sdk, state, token); return true; }, token);
    }
}
