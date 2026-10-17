using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerRf3Routes
{
    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, token), receipt);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, token), receipt);
    }

    internal static async Task RefusedAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        ErrorCode expected, CancellationToken token)
    {
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.ExecuteSqlAsync(sql, token), expected);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), expected, true);
    }
}
