using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.RelationalStorage;
using KeyLoad.ClusterTests.StorageReplication;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateDropReplay
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateDropState state,
        CancellationToken token)
    {
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        foreach (var table in state.Tables)
        {
            await FunctionalGateDropAssertions.StaleAsync(context, table.OriginalReference,
                () => FunctionalGateDropAssertions.FreshAsync(context, state, token), token);
            await FunctionalGateDropAssertions.FreshAsync(context, state, token);
            foreach (var route in Enum.GetValues<DropLifecycleClusterRoute>())
            {
                var actual = await FunctionalGateDropCalls.InvokeAsync(context, table.ParentId,
                    table.Request, route, token);
                await FunctionalGateDropAssertions.ResultAsync(context, table.ParentId, table.Request, actual);
                await SqlClusterProtocol.EqualAsync(table.OriginalDrop, actual);
                await FunctionalGateDropAssertions.FreshAsync(context, state, token);
            }
            await SubmissionColdAssertions.ReceiptAsync(table.FreshReceipt,
                await context.SdkAsync(context.Administrator.Sdk.CommitAsync(table.FreshCommand, token)));
            await SubmissionColdAssertions.ReceiptAsync(table.FreshReceipt,
                await context.McpAsync<CommitReceipt>(context.Administrator,
                    McpCallerTools.DocumentsCommit, table.FreshCommand, token));
        }
        await OriginalAsync(context, state, token);
    }

    private static async Task OriginalAsync(FunctionalGateContext context, FunctionalGateDropState state,
        CancellationToken token)
    {
        await SubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await context.SdkAsync(context.Administrator.Sdk.CommitAsync(state.Original, token)));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        await SubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await context.McpAsync<CommitReceipt>(context.Administrator,
                McpCallerTools.DocumentsCommit, state.Original, token));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        var sql = FunctionalGateRelationalCalls.Commit(state.Original);
        await SubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context,
                context.Administrator, sql, token));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        await SubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context,
                context.Administrator, sql, token));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
    }
}
