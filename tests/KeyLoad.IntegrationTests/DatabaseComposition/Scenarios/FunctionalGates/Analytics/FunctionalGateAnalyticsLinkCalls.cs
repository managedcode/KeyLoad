using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsLinkCalls
{
    internal static async Task<CommitReceipt> CommitAsync(FunctionalGateContext context, CommandRequest command,
        MutationReceipt effect, CancellationToken token)
    {
        var original = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(command, token));
        await FunctionalGateCompositionCalls.ReceiptAsync(context, original, command.CommandId, [effect]);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<CommitReceipt>(context.Administrator,
            McpCallerTools.DocumentsCommit, FunctionalGateAnalyticsIdentity.Wire(command), token), original);
        var sql = FunctionalGateRelationalCalls.Commit(command);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(
            context, context.Administrator, sql, token), original);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(
            context, context.Administrator, sql, token), original);
        return original;
    }

    internal static async Task RawAsync(FunctionalGateContext context, ReadAnalyticsEventsRequest request,
        AnalyticsEventsPage expected, CancellationToken token)
    {
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Administrator.Sdk.ReadAnalyticsEventsAsync(request, token)), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsEventsPage>(context.Administrator,
            AnalyticsStoreProtocol.ReadTool, request, token), expected);
        var sql = SqlClusterProtocol.Call(context.Partition, AnalyticsStoreProtocol.ReadTool, request);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateRelationalCalls.SqlSdkAsync<AnalyticsEventsPage>(
            context, context.Administrator, sql, token), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateRelationalCalls.SqlMcpAsync<AnalyticsEventsPage>(
            context, context.Administrator, sql, token), expected);
    }

    internal static async Task AggregateAsync(FunctionalGateContext context, FunctionalGateAnalyticsLinkProof proof,
        AnalyticsAggregateResult expected, CancellationToken token)
    {
        var request = FunctionalGateAnalyticsLinkRequests.Aggregate(context, proof);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Administrator.Sdk.AggregateAnalyticsAsync(request, token)), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.McpAsync<AnalyticsAggregateResult>(context.Administrator,
            AnalyticsStoreProtocol.AggregateTool, request, token), expected);
        var sql = SqlClusterProtocol.Call(context.Partition, AnalyticsStoreProtocol.AggregateTool, request);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateRelationalCalls.SqlSdkAsync<AnalyticsAggregateResult>(
            context, context.Administrator, sql, token), expected);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(await FunctionalGateRelationalCalls.SqlMcpAsync<AnalyticsAggregateResult>(
            context, context.Administrator, sql, token), expected);
    }
}
