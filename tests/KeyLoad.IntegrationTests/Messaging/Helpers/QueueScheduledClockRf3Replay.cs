using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueScheduledClockRf3Replay
{
    internal static async Task ReceiveAsync(RequestCqrsRf3Callers callers, ReceiveRequest request,
        ReceiveResult expected, CancellationToken token)
    {
        await QueueLeaseRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token)), expected);
        var mcp = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token));
        await QueueLeaseRf3Assertions.EqualAsync(mcp.Value, expected);
        var sql = SqlRf3Protocol.Call(request.Lane.Partition, McpCallerTools.MessagesReceive, request);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<ReceiveResult>(callers.Sdk, sql, token), expected);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<ReceiveResult>(callers.Mcp, sql, token), expected);
    }

    internal static async Task AckAsync(RequestCqrsRf3Callers callers, DeliveryCommand command,
        CommitReceipt expected, CancellationToken token)
    {
        await QueueLeaseRf3Assertions.ReplayedAsync(callers.Sdk, callers.Mcp, command, expected, token);
        var sql = SqlRf3Protocol.Call(command.Lane.Partition, McpCallerTools.MessagesComplete, command);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token), expected);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token), expected);
    }

    internal static async Task EnqueueAsync(RequestCqrsRf3Callers admin, CommandRequest command,
        CommitReceipt expected, CancellationToken token)
    {
        await QueueLeaseRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token)), expected);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token));
        await QueueLeaseRf3Assertions.EqualAsync(mcp.Value, expected);
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<CommitReceipt>(admin.Sdk, sql, token), expected);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<CommitReceipt>(admin.Mcp, sql, token), expected);
    }
}
