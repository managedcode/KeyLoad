using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextProtectedPublicColdTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await AdminAsync(fixture, async administrator =>
        {
            var original = await NativeTextProtectedPublicColdMutation.CreateAsync(fixture, administrator, token);
            await HealthyAsync(fixture, administrator, original, 1L, token);
            await PolicyAsync(administrator, original, allowed: false, epoch: 2L, token);
            await DeniedAsync(fixture, administrator, original, token);
            return original;
        }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async administrator =>
        {
            await DeniedAsync(fixture, administrator, state, token);
            await PolicyAsync(administrator, state, allowed: true, epoch: 3L, token);
            await HealthyAsync(fixture, administrator, state, 3L, token);
            return true;
        }, token);
        await NativeTextProtectedPublicColdRestart.RunAsync(fixture, token);
        _ = await AdminAsync(fixture, async administrator =>
        {
            await HealthyAsync(fixture, administrator, state, 3L, token);
            return true;
        }, token);
    }

    private static Task<T> AdminAsync<T>(ClusterFixture fixture, Func<RequestCqrsRf3Callers, Task<T>> operation,
        CancellationToken token) => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, operation, token);

    private static async Task PolicyAsync(RequestCqrsRf3Callers administrator,
        NativeTextProtectedPublicColdState state, bool allowed, long epoch, CancellationToken token)
    {
        var expected = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            FieldGrants = allowed ? state.Identity.Principal.FieldGrants
                : state.Identity.Principal.FieldGrants.Remove(NativeTextRf3Scenario.TextUseGrant)
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }

    private static Task<bool> DeniedAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        NativeTextProtectedPublicColdState state, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async reader =>
            {
                await NativeTextProtectedPublicColdAssertions.DeniedAsync(reader, administrator, state, token);
                return true;
            }, token);

    private static Task<bool> HealthyAsync(ClusterFixture fixture, RequestCqrsRf3Callers administrator,
        NativeTextProtectedPublicColdState state, long epoch, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret,
            async reader =>
            {
                await NativeTextProtectedPublicColdAssertions.HealthyAsync(reader, state, epoch, token);
                await NativeTextProtectedPublicColdMutation.ReplayAsync(administrator, state, token);
                await NativeTextProtectedPublicColdAssertions.HealthyAsync(reader, state, epoch, token);
                return true;
            }, token);
}
