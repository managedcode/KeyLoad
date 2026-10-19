using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.IntegrationTests.Features.StorageReplication;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdDropReplay
{
    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdState original,
        FunctionalGateColdDropState state, CancellationToken token)
    {
        foreach (var table in state.Tables)
        {
            var model = original.Models.Single(value => value.Index == table.Index);
            await FunctionalGateDropAssertions.StaleAsync(context, table.OriginalReference,
                () => FunctionalGateColdDropAssertions.AllAsync(context, state, token), token);
            foreach (var route in Enum.GetValues<DropLifecycleClusterRoute>())
            {
                var actual = await FunctionalGateDropCalls.InvokeAsync(context, table.ParentId,
                    table.Request, route, token);
                await FunctionalGateDropAssertions.ResultAsync(context, table.ParentId, table.Request, actual);
                await KeyLoad.IntegrationTests.Features.QueryExecution.SqlClusterProtocol.EqualAsync(
                    table.OriginalDrop, actual);
                await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
            }
            await FunctionalGateColdDropRefusals.OriginalWriterAsync(context, model, table, state, token);
            await InsertAsync(context, table, state, token);
            var fresh = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(table.FreshCommand, token));
            await SubmissionColdAssertions.ReceiptAsync(fresh, table.FreshReceipt);
            await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
            fresh = await context.McpAsync<CommitReceipt>(context.Administrator,
                McpCallerTools.DocumentsCommit, table.FreshCommand, token);
            await SubmissionColdAssertions.ReceiptAsync(fresh, table.FreshReceipt);
            await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
        }
    }

    private static async Task InsertAsync(FunctionalGateContext context, FunctionalGateColdDropTable table, FunctionalGateColdDropState state, CancellationToken token)
    {
        var actual = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(table.AdministratorCommand, token));
        await SubmissionColdAssertions.ReceiptAsync(actual, table.AdministratorReceipt);
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
        actual = await context.McpAsync<CommitReceipt>(context.Administrator,
            McpCallerTools.DocumentsCommit, table.AdministratorCommand, token);
        await SubmissionColdAssertions.ReceiptAsync(actual, table.AdministratorReceipt);
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
        actual = await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context,
            context.Administrator, table.AdministratorInsert, token);
        await SubmissionColdAssertions.ReceiptAsync(actual, table.AdministratorReceipt);
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
        actual = await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context,
            context.Administrator, table.AdministratorInsert, token);
        await SubmissionColdAssertions.ReceiptAsync(actual, table.AdministratorReceipt);
        await FunctionalGateColdDropAssertions.AllAsync(context, state, token);
    }
}
