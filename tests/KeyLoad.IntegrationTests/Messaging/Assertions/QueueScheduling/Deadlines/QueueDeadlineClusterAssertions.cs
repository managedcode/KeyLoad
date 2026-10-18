using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

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
            var actual = await ReadAsync(callers, lane, expected.Metadata.Id, token);
            if (actual.Metadata.State == MessageState.Ready)
            {
                await QueueLeaseClusterAssertions.EqualAsync(actual, expected);
                return;
            }
            await Assert.That(actual.Metadata.State).IsEqualTo(MessageState.Scheduled);
            await Task.Delay(DueRecurringClusterProtocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException("The authorized native queue deadline did not advance within its original due progress window.");
    }
}
