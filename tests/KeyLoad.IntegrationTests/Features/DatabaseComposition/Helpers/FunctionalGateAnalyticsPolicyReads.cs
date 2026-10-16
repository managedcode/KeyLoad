using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateUserAnalyticsProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsPolicyReads
{
    private const long Revision = 1;

    internal static async Task VerifyAsync(FunctionalGateContext context,
        IEnumerable<FunctionalGateAnalyticsPolicyState> states, bool rawRead, CancellationToken token)
    {
        foreach (var state in states)
        {
            var row = FunctionalGateUserAnalyticsState.Record(state.Owned, Revision);
            if (!rawRead)
            {
                row = row with
                {
                    Metrics = row.Metrics.Remove(P.Value)
                };
            }
            await FunctionalGateAnalyticsColdAssertions.StateAsync(context, state.Query,
                [row], FunctionalGateAnalyticsPolicyState.Expected, token);
        }
    }

    internal static async Task ForeignWritesAsync(FunctionalGateContext context,
        IEnumerable<FunctionalGateAnalyticsPolicyState> states, CancellationToken token)
    {
        foreach (var state in states)
        {
            var command = new CommandRequest(Guid.NewGuid(), context.Partition,
                [new DeleteAnalyticsEvents(state.Query.Store, FunctionalGateAnalyticsColdSeed.Revisions((state.Foreign.Id, Revision)))]);
            await FunctionalGateAnalyticsPolicyWitness.FullAsync(context, state, token);
            await FunctionalGateUserAssertions.RefusedWriteAsync(context.Writer, command, null, token);
            await FunctionalGateAnalyticsPolicyWitness.FullAsync(context, state, token);
            await FunctionalGateAnalyticsColdAssertions.StateAsync(context, state.Query,
                [FunctionalGateUserAnalyticsState.Record(state.Owned, Revision)], FunctionalGateAnalyticsPolicyState.Expected, token);
        }
    }

    internal static async Task DeniedAsync(FunctionalGateContext context,
        IEnumerable<FunctionalGateAnalyticsPolicyState> states, CancellationToken token)
    {
        foreach (var state in states)
        {
            var value = state.Query;
            var request = new AggregateAnalyticsRequest(context.Partition, value.Store, value.From, value.Until,
                [P.Kind], P.Selections, P.MaximumGroups);
            await FunctionalGateAnalyticsPolicyWitness.FullAsync(context, state, token);
            var sdk = await context.Reader.Sdk.AggregateAnalyticsAsync(request, token);
            await Assert.That(sdk.IsFailed).IsTrue();
            await Assert.That(sdk.Value).IsNull();
            await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync(
                AnalyticsStoreProtocol.AggregateTool, request, token), ErrorCode.PermissionDenied, dispatched: true);
            var sql = SqlRf3Protocol.Call(context.Partition, AnalyticsStoreProtocol.AggregateTool, request);
            var sqlSdk = await context.Reader.Sdk.ExecuteSqlAsync(sql, token);
            await Assert.That(sqlSdk.IsFailed).IsTrue();
            await Assert.That(sqlSdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await Assert.That(sqlSdk.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync(
                SqlOperationProtocol.ToolName, sql, token), ErrorCode.PermissionDenied, dispatched: true);
            await FunctionalGateAnalyticsPolicyWitness.FullAsync(context, state, token);
            await FunctionalGateAnalyticsPolicyWitness.OwnedAsync(context, state, token);
        }
    }
}
