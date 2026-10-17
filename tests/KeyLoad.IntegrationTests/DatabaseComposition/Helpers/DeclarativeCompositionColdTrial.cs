using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class DeclarativeCompositionColdTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CompositionSqlColdState state,
        DeclarativeCompositionBudgetState budget, CancellationToken token)
    {
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin => { await OriginalAsync(admin, state, budget, token); return true; }, token);
        var healthy = await CompositionSqlColdAuthority.RunAsync(fixture, state, token);
        await ModelSqlRf3Cold.RunAsync(fixture, token);
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey,
            async admin =>
            {
                await OriginalAsync(admin, state, budget, token);
                await CompositionSqlColdAssertions.OtherAsync(admin, state, true, token);
                await SqlRf3Protocol.EqualAsync(healthy.Graph, await McpCallerAssertions.SdkSuccessAsync(
                    await admin.Sdk.TraverseAsync(new(state.Scenario.Partition,
                        CompositionSqlColdProtocol.OtherGraph, state.Scenario.First), token)));
                return true;
            }, token);
        await global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, healthy.Identity.Secret,
            async callers =>
            {
                await CompositionSqlColdAssertions.ReplayAsync(callers, healthy.Command, healthy.Receipt, token);
                return true;
            }, token);
    }

    private static async Task OriginalAsync(global::KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsRf3Callers callers, CompositionSqlColdState state,
        DeclarativeCompositionBudgetState budget, CancellationToken token)
    {
        await CompositionSqlColdAssertions.OriginalAsync(callers, state, token);
        var forward = DeclarativeCompositionSql.Forward(state.Forward, (QueueToGraph)state.Forward.Mutations[0]);
        var reverse = DeclarativeCompositionSql.Reverse(state.Reverse, (GraphToQueueMutation)state.Reverse.Mutations[0]);
        await SqlRf3Protocol.EqualAsync(state.ForwardReceipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, forward, token));
        await SqlRf3Protocol.EqualAsync(state.ForwardReceipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, forward, token));
        await SqlRf3Protocol.EqualAsync(state.ReverseReceipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, reverse, token));
        await SqlRf3Protocol.EqualAsync(state.ReverseReceipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, reverse, token));
        await DeclarativeCompositionBudgetFlow.ColdAsync(callers, state.Scenario, budget, token);
    }
}
