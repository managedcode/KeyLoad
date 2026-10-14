using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClosedCommandRf3Flow
{
    internal static async Task<PostgresClosedCommandRf3State> WarmAsync(ClusterFixture fixture,
        KeyLoadClient root, PostgresCompositionRf3Image image, CancellationToken token)
    {
        var state = await PostgresClosedCommandRf3Provisioning.CreateAsync(root, image, token);
        Task Models() => PostgresClosedCommandRf3Assertions.ModelsAsync(root, state, token);
        Task Target(bool allowed) => PostgresClosedCommandRf3Assertions.TargetAsync(fixture, state, allowed, Models, token);
        async Task Observe(bool allowed)
        { await Models(); await Target(allowed); }
        await Target(allowed: true);
        await PostgresClosedCommandRf3Operations.RefusedAsync(fixture, state, root, token);
        await Target(allowed: true);
        await Models();
        return await PostgresClosedCommandRf3Operations.OwnedAsync(fixture, state, async admin =>
        {
            await PostgresClosedCommandRf3Operations.ReplayAsync(admin, state, () => Observe(allowed: false), token);
            await SqlRf3Protocol.EqualAsync(state.Repaired,
                await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Repaired, token)));
            await Observe(allowed: true);
            await PostgresClosedCommandRf3Operations.ReplayAsync(admin, state, () => Observe(allowed: true), token);
            return state;
        }, token);
    }

    internal static Task ColdAsync(ClusterFixture fixture,
        PostgresClosedCommandRf3State state, CancellationToken token)
        => PostgresClosedCommandRf3Operations.OwnedAsync(fixture, state, async admin =>
        {
            Task Models() => PostgresClosedCommandRf3Assertions.ModelsAsync(admin.Sdk, state, token);
            async Task Observe()
            {
                await Models();
                await PostgresClosedCommandRf3Assertions.TargetAsync(fixture, state, allowed: true, Models, token);
            }
            await PostgresClosedCommandRf3Operations.RefusedAsync(fixture, state, admin.Sdk, token);
            await PostgresClosedCommandRf3Operations.ReplayAsync(admin, state, Observe, token);
            return true;
        }, token);
}
