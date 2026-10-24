using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicProducerClusterRoutes
{
    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        CommitReceipt receipt, CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, sql, token), receipt);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, sql, token), receipt);
    }

    internal static async Task RefusedAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        ErrorCode expected, CancellationToken token)
    {
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerClusterAssertions.DeniedAsync(await sdk.ExecuteSqlAsync(sql, token), expected);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), expected, true);
    }
}
