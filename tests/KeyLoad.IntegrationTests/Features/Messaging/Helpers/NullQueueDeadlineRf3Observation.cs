using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class NullQueueDeadlineRf3Observation
{
    private const string ControlQueueSuffix = "-scheduler-control";
    private const string UtcZone = "UTC";
    private const string Payload = "{\"scheduler\":true}";
    private const string Headers = "{}";
    private const long OriginalRevision = 1;
    private const long CancelledRevision = 2;
    private const long Generation = 1;
    private const long FirstOrdinal = 0;
    private const long NextOrdinal = 1;

    internal static QueueLaneRef ControlLane(QueueLaneRef lane)
        => new(lane.Partition, lane.Queue + ControlQueueSuffix);

    internal static RecurringScheduleDefinition Definition(QueueLaneRef lane, DateTimeOffset due)
        => new(ControlLane(lane), Guid.NewGuid(), due, DueRecurringRf3Protocol.Interval, UtcZone,
            RecurringMisfirePolicy.CatchUp, Payload, Headers);

    internal static Task<CommitReceipt> WaitAsync(ClusterFixture fixture, NullQueueDeadlineRf3State state,
        CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async admin =>
        {
            await WaitForOccurrenceAsync(admin, state.SchedulerControl, token);
            await RequireScheduleAsync(admin, state.SchedulerControl, false, token);
            await RequireOccurrenceAsync(admin, state.SchedulerControl, token);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CommitAsync(state.StopSchedulerControl, token));
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, state.StopSchedulerControl, receipt, token);
            await RequireScheduleAsync(admin, state.SchedulerControl, true, token);
            return receipt;
        }, token);

    internal static Task<bool> RequireCancelledAsync(ClusterFixture fixture, NullQueueDeadlineRf3State state,
        CommitReceipt receipt, CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async admin =>
        {
            await RequireScheduleAsync(admin, state.SchedulerControl, true, token);
            await RequireOccurrenceAsync(admin, state.SchedulerControl, token);
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, state.StopSchedulerControl, receipt, token);
            return true;
        }, token);

    private static async Task WaitForOccurrenceAsync(RequestCqrsRf3Callers admin,
        RecurringScheduleDefinition definition, CancellationToken token)
    {
        var until = TimeProvider.System.GetUtcNow() + DueRecurringRf3Protocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.InspectRecurringScheduleAsync(
                new(definition.Lane, definition.ScheduleId), token));
            await Assert.That(actual).IsNotNull();
            if (actual!.NextOrdinal == NextOrdinal)
            { await Assert.That(TimeProvider.System.GetUtcNow() >= definition.FirstDueAt).IsTrue(); return; }
            await Assert.That(actual.NextOrdinal).IsEqualTo(FirstOrdinal);
            await Task.Delay(DueRecurringRf3Protocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException("The native recurring control did not emit within its original progress window.");
    }

    private static async Task RequireScheduleAsync(RequestCqrsRf3Callers admin,
        RecurringScheduleDefinition definition, bool cancelled, CancellationToken token)
    {
        var request = new InspectRecurringScheduleRequest(definition.Lane, definition.ScheduleId);
        var expected = new RecurringScheduleInspection(definition,
            cancelled ? CancelledRevision : OriginalRevision, Generation, NextOrdinal, cancelled, false, []);
        await QueueLeaseRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.InspectRecurringScheduleAsync(request, token)), expected);
        var mcp = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(
            await admin.Mcp.CallAsync(McpCallerTools.ScheduleInspect, request, token));
        await QueueLeaseRf3Assertions.EqualAsync(mcp.Value, expected);
        var sql = SqlRf3Protocol.Call(definition.Lane.Partition, McpCallerTools.ScheduleInspect, request);
        await QueueLeaseRf3Assertions.EqualAsync(
            await SqlRf3Protocol.SdkAsync<RecurringScheduleInspection?>(admin.Sdk, sql, token), expected);
        await QueueLeaseRf3Assertions.EqualAsync(
            await SqlRf3Protocol.McpAsync<RecurringScheduleInspection?>(admin.Mcp, sql, token), expected);
    }

    private static Task RequireOccurrenceAsync(RequestCqrsRf3Callers admin,
        RecurringScheduleDefinition definition, CancellationToken token)
    {
        var id = DueRecurringRf3Protocol.OccurrenceId(definition.ScheduleId, Generation, FirstOrdinal);
        var expected = new MessageInspection(new(id, MessageState.Ready,
            (int)FirstOrdinal, OriginalRevision, NextOrdinal, definition.FirstDueAt, null), Payload, Headers);
        return QueueDeadlineRf3Assertions.RequireAsync(admin, definition.Lane, expected, token);
    }
}
