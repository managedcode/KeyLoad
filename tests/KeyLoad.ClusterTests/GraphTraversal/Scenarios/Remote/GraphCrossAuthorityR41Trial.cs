using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
namespace KeyLoad.ClusterTests.GraphTraversal;

internal static class GraphCrossAuthorityR41Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = new GraphCrossAuthorityR41State(await GraphIncomingClusterScenario.CreateAsync(fixture, token));
        await OwnedAsync(fixture, state, async (administrator, reader) =>
        {
            await GraphCrossAuthorityR41Commands.SeedAsync(administrator, state, token);
            await VerifyAsync(administrator, reader, state, token);
            await PolicyAsync(administrator, state, 2L, token);
            await VerifyAsync(administrator, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
        await OwnedAsync(fixture, state, async (administrator, reader) =>
        {
            await VerifyAsync(administrator, reader, state, token);
            await PolicyAsync(administrator, state, 3L, token);
            await GraphCrossAuthorityR41Assertions.MatrixAsync(reader, administrator, state, token);
            await GraphCrossAuthorityR41Commands.HealthyAsync(administrator, state, token);
            await VerifyAsync(administrator, reader, state, token);
        }, token);
        await ColdAsync(fixture, state, token);
    }

    private static async Task PolicyAsync(RequestCqrsClusterCallers administrator,
        GraphCrossAuthorityR41State state, long epoch, CancellationToken token)
    {
        var original = state.Seed.Reader.Principal;
        var desired = original with
        {
            PolicyEpoch = epoch,
            Grants = epoch == 2L
            ? [.. original.Grants.Where(grant => grant.Database != state.Seed.FirstSourcePartition.DatabaseId
                || grant.Resource != GraphIncomingClusterScenario.Collection)] : original.Grants
        };
        await SqlClusterProtocol.EqualAsync(desired, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), desired, token)));
        state.Hidden = epoch == 2L;
    }

    private static async Task VerifyAsync(RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader,
        GraphCrossAuthorityR41State state, CancellationToken token)
    {
        await GraphCrossAuthorityR41Bounds.VerifyAsync(reader, administrator, state, token);
        await GraphCrossAuthorityR41Bounds.OriginalsAsync(reader, administrator, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, GraphCrossAuthorityR41State state, CancellationToken token)
    {
        var before = await GraphIncomingPublicCold.ObserveAsync(fixture, token);
        await GraphIncomingPublicCold.RestartAsync(fixture, before, token);
        await OwnedAsync(fixture, state, (administrator, reader) => VerifyAsync(administrator, reader, state, token), token);
    }

    private static async Task OwnedAsync(ClusterFixture fixture, GraphCrossAuthorityR41State state,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async administrator => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Seed.Reader.Secret, async reader => { await operation(administrator, reader); return true; }, token), token);
}
