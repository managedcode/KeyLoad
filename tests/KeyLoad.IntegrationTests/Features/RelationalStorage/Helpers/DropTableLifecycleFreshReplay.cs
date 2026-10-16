using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleFreshReplay
{
    internal static async Task VerifyAsync(RequestCqrsRf3Callers administrator,
        DropTableLifecycleFreshState fresh, CancellationToken token)
        => await OriginalAsync(administrator, fresh.Write, fresh.Receipt, token);

    internal static async Task OriginalAsync(RequestCqrsRf3Callers administrator,
        CommandRequest command, CommitReceipt receipt, CancellationToken token, Func<Task>? unchanged = null)
    {
        await NativeSubmissionColdAssertions.ReceiptAsync(await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.CommitAsync(command, token)), receipt);
        if (unchanged is not null)
        {
            await unchanged();
        }
        await NativeSubmissionColdAssertions.ReceiptAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        if (unchanged is not null)
        {
            await unchanged();
        }
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command) with
        {
            CommandId = command.CommandId
        };
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ExecuteSqlAsync(sql, token));
        await NativeSubmissionColdAssertions.ReceiptAsync(sdk.Deserialize<CommitReceipt>(JsonDefaults.Options)
            ?? throw new InvalidOperationException(), receipt);
        if (unchanged is not null)
        {
            await unchanged();
        }
        await NativeSubmissionColdAssertions.ReceiptAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token))).Value, receipt);
        if (unchanged is not null)
        {
            await unchanged();
        }
    }
}
