using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueScheduledClockClusterReplay
{
    internal static async Task ReceiveAsync(RequestCqrsClusterCallers callers, ReceiveRequest request,
        ReceiveResult expected, CancellationToken token)
    {
        await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token)), expected);
        var mcp = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
        await QueueLeaseClusterAssertions.EqualAsync(mcp.Value, expected);
        var sql = SqlClusterProtocol.Call(request.Lane.Partition, McpCallerTools.MessagesReceive, request);
        await QueueLeaseClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<ReceiveResult>(callers.Sdk, sql, token), expected);
        await QueueLeaseClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<ReceiveResult>(callers.Mcp, sql, token), expected);
    }

    internal static async Task AckAsync(RequestCqrsClusterCallers callers, DeliveryCommand command,
        CommitReceipt expected, CancellationToken token)
    {
        await QueueLeaseClusterAssertions.ReplayedAsync(callers.Sdk, callers.Mcp, command, expected, token);
        var sql = SqlClusterProtocol.Call(command.Lane.Partition, McpCallerTools.MessagesComplete, command);
        await QueueLeaseClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token), expected);
        await QueueLeaseClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token), expected);
    }

    internal static async Task EnqueueAsync(RequestCqrsClusterCallers admin, CommandRequest command,
        CommitReceipt expected, CancellationToken token)
    {
        await QueueLeaseClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token)), expected);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await QueueLeaseClusterAssertions.EqualAsync(mcp.Value, expected);
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueLeaseClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<CommitReceipt>(admin.Sdk, sql, token), expected);
        await QueueLeaseClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<CommitReceipt>(admin.Mcp, sql, token), expected);
    }
}
