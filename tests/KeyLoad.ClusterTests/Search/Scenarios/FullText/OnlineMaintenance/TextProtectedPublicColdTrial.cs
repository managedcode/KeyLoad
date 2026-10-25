using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Search;

internal static class TextProtectedPublicColdTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async administrator =>
        {
            var original = await TextProtectedPublicColdMutation.CreateAsync(fixture, administrator, token);
            await HealthyAsync(fixture, administrator, original, 1L, token);
            await PolicyAsync(administrator, original, allowed: false, epoch: 2L, token);
            await DeniedAsync(fixture, administrator, original, token);
            return original;
        }, token);
        await TextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async administrator =>
        {
            await DeniedAsync(fixture, administrator, state, token);
            await PolicyAsync(administrator, state, allowed: true, epoch: 3L, token);
            await HealthyAsync(fixture, administrator, state, 3L, token);
            return true;
        }, token);
        await TextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async administrator =>
        {
            await HealthyAsync(fixture, administrator, state, 3L, token);
            return true;
        }, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture, Func<RequestCqrsClusterCallers, Task<T>> operation,
        CancellationToken token) => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static async Task PolicyAsync(RequestCqrsClusterCallers administrator,
        TextProtectedPublicColdState state, bool allowed, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            FieldGrants = allowed ? state.Identity.Principal.FieldGrants
                : state.Identity.Principal.FieldGrants.Remove(TextClusterScenario.TextUseGrant)
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }

    private static Task<bool> DeniedAsync(ClusterFixture fixture, RequestCqrsClusterCallers administrator,
        TextProtectedPublicColdState state, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async reader =>
            {
                await TextProtectedPublicColdAssertions.DeniedAsync(reader, administrator, state, token);
                return true;
            }, token);

    private static Task<bool> HealthyAsync(ClusterFixture fixture, RequestCqrsClusterCallers administrator,
        TextProtectedPublicColdState state, long epoch, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async reader =>
            {
                await TextProtectedPublicColdAssertions.HealthyAsync(reader, state, epoch, token);
                await TextProtectedPublicColdMutation.ReplayAsync(administrator, state, token);
                await TextProtectedPublicColdAssertions.HealthyAsync(reader, state, epoch, token);
                return true;
            }, token);
}
