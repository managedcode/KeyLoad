using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDeadlineClusterAssertions
{
    internal static async Task<MessageInspection> ReadAsync(RequestCqrsClusterCallers callers, QueueLaneRef lane,
        string id, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        var sdk = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token));
        var mcp = await PublicReadAssertions.MessageAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token));
        await Assert.That(sdk).IsNotNull();
        await QueueLeaseClusterAssertions.EqualAsync(mcp.Value, sdk);
        var sql = SqlClusterProtocol.Call(lane.Partition, McpCallerTools.MessagesInspect, request);
        await QueueLeaseClusterAssertions.EqualAsync((await SqlClusterProtocol.SdkAsync<MessageInspectionResult>(callers.Sdk, sql, token)).Message, sdk);
        await QueueLeaseClusterAssertions.EqualAsync((await SqlClusterProtocol.McpAsync<MessageInspectionResult>(callers.Mcp, sql, token)).Message, sdk);
        return sdk!;
    }

    internal static async Task RequireAsync(RequestCqrsClusterCallers callers, QueueLaneRef lane,
        MessageInspection expected, CancellationToken token)
        => await QueueLeaseClusterAssertions.EqualAsync(await ReadAsync(callers, lane, expected.Metadata.Id, token), expected);

    internal static async Task WaitReadyAsync(RequestCqrsClusterCallers callers, QueueLaneRef lane,
        MessageInspection expected, CancellationToken token)
    {
        var until = TimeProvider.System.GetUtcNow() + DueRecurringClusterProtocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            // The promotion can commit between the four public reads, so poll one route until Ready and only then
            // require every route to return the same message.
            var observed = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(
                new InspectMessageRequest(lane, expected.Metadata.Id), token));
            if (observed!.Metadata.State == MessageState.Ready)
            {
                await QueueLeaseClusterAssertions.EqualAsync(await ReadAsync(callers, lane, expected.Metadata.Id, token), expected);
                return;
            }
            await Assert.That(observed.Metadata.State).IsEqualTo(MessageState.Scheduled);
            await Task.Delay(DueRecurringClusterProtocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException("The authorized execution queue deadline did not advance within its original due progress window.");
    }
}
