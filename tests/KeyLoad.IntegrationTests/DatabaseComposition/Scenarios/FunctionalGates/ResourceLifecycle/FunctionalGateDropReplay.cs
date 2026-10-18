using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

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
                await FunctionalGateDropAssertions.ResultAsync(table.ParentId, table.Request, actual);
                await SqlClusterProtocol.EqualAsync(table.OriginalDrop, actual);
                await FunctionalGateDropAssertions.FreshAsync(context, state, token);
            }
            await NativeSubmissionColdAssertions.ReceiptAsync(table.FreshReceipt,
                await context.SdkAsync(context.Administrator.Sdk.CommitAsync(table.FreshCommand, token)));
            await NativeSubmissionColdAssertions.ReceiptAsync(table.FreshReceipt,
                await context.McpAsync<CommitReceipt>(context.Administrator,
                    McpCallerTools.DocumentsCommit, table.FreshCommand, token));
        }
        await OriginalAsync(context, state, token);
    }

    private static async Task OriginalAsync(FunctionalGateContext context, FunctionalGateDropState state,
        CancellationToken token)
    {
        await NativeSubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await context.SdkAsync(context.Administrator.Sdk.CommitAsync(state.Original, token)));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        await NativeSubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await context.McpAsync<CommitReceipt>(context.Administrator,
                McpCallerTools.DocumentsCommit, state.Original, token));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        var sql = FunctionalGateRelationalCalls.Commit(state.Original);
        await NativeSubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context,
                context.Administrator, sql, token));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
        await NativeSubmissionColdAssertions.ReceiptAsync(state.Receipt,
            await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context,
                context.Administrator, sql, token));
        await FunctionalGateDropAssertions.FreshAsync(context, state, token);
    }
}
