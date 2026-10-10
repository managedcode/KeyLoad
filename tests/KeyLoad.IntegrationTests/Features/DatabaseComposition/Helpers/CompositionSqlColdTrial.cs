using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class CompositionSqlColdTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CompositionSqlColdState state, CancellationToken token)
    {
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        { await CompositionSqlColdAssertions.OriginalAsync(admin, state, token); return true; }, token);
        var healthy = await CompositionSqlColdAuthority.RunAsync(fixture, state, token);
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
        {
            await CompositionSqlColdAssertions.OriginalAsync(admin, state, token);
            await CompositionSqlColdAssertions.OtherAsync(admin, state, true, token);
            await SqlRf3Protocol.EqualAsync(healthy.Graph, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.TraverseAsync(new(state.Scenario.Partition, CompositionSqlColdProtocol.OtherGraph, state.Scenario.First), token)));
            return true;
        }, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, healthy.Identity.Secret, async callers =>
        { await CompositionSqlColdAssertions.ReplayAsync(callers, healthy.Command, healthy.Receipt, token); return true; }, token);
    }
}
