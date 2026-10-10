using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Assertions
{
    internal static async Task<MessageInspection> ReadAsync(RequestCqrsRf3Callers callers, QueueLaneRef lane,
        string id, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, id);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(request, token));
        var mcp = await McpCallerAssertions.SuccessAsync<MessageInspection?>(await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token));
        await Assert.That(sdk).IsNotNull();
        await QueueLeaseRf3Assertions.EqualAsync(mcp.Value, sdk);
        var sql = SqlRf3Protocol.Call(lane.Partition, McpCallerTools.MessagesInspect, request);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<MessageInspection?>(callers.Sdk, sql, token), sdk);
        await QueueLeaseRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<MessageInspection?>(callers.Mcp, sql, token), sdk);
        return sdk!;
    }

    internal static async Task RequireAsync(RequestCqrsRf3Callers callers, QueueLaneRef lane,
        MessageInspection expected, CancellationToken token)
        => await QueueLeaseRf3Assertions.EqualAsync(await ReadAsync(callers, lane, expected.Metadata.Id, token), expected);

    internal static async Task WaitReadyAsync(RequestCqrsRf3Callers callers, QueueLaneRef lane,
        MessageInspection expected, CancellationToken token)
    {
        var until = TimeProvider.System.GetUtcNow() + DueRecurringRf3Protocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var actual = await ReadAsync(callers, lane, expected.Metadata.Id, token);
            if (actual.Metadata.State == MessageState.Ready)
            { await QueueLeaseRf3Assertions.EqualAsync(actual, expected); return; }
            await Assert.That(actual.Metadata.State).IsEqualTo(MessageState.Scheduled);
            await Task.Delay(DueRecurringRf3Protocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException("The authorized native queue deadline did not advance within its original due progress window.");
    }
}
