using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class NullQueueDeadlineClusterObservation
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
        => new(ControlLane(lane), Guid.NewGuid(), due, DueRecurringClusterProtocol.Interval, UtcZone,
            RecurringMisfirePolicy.CatchUp, Payload, Headers);

    internal static Task<CommitReceipt> WaitAsync(ClusterFixture fixture, NullQueueDeadlineClusterState state,
        CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, state.SchedulerIdentity.Secret, async admin =>
        {
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, state.SchedulerConfiguration,
                state.SchedulerConfigurationReceipt, token);
            await WaitForOccurrenceAsync(admin, state.SchedulerControl, token);
            await RequireScheduleAsync(admin, state.SchedulerControl, false, token);
            await RequireOccurrenceAsync(admin, state.SchedulerControl, token);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.CommitAsync(state.StopSchedulerControl, token));
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, state.StopSchedulerControl, receipt, token);
            await RequireScheduleAsync(admin, state.SchedulerControl, true, token);
            return receipt;
        }, token);

    internal static Task<bool> RequireCancelledAsync(ClusterFixture fixture, NullQueueDeadlineClusterState state,
        CommitReceipt receipt, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, state.SchedulerIdentity.Secret, async admin =>
        {
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, state.SchedulerConfiguration,
                state.SchedulerConfigurationReceipt, token);
            await RequireScheduleAsync(admin, state.SchedulerControl, true, token);
            await RequireOccurrenceAsync(admin, state.SchedulerControl, token);
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, state.StopSchedulerControl, receipt, token);
            return true;
        }, token);

    private static async Task WaitForOccurrenceAsync(RequestCqrsClusterCallers admin,
        RecurringScheduleDefinition definition, CancellationToken token)
    {
        var until = TimeProvider.System.GetUtcNow() + DueRecurringClusterProtocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var actual = await PublicReadAssertions.ScheduleAsync(await admin.Sdk.InspectRecurringScheduleAsync(
                new(definition.Lane, definition.ScheduleId), token));
            await Assert.That(actual).IsNotNull();
            if (actual!.NextOrdinal == NextOrdinal)
            {
                await Assert.That(TimeProvider.System.GetUtcNow() >= definition.FirstDueAt).IsTrue();
                return;
            }
            await Assert.That(actual.NextOrdinal).IsEqualTo(FirstOrdinal);
            await Task.Delay(DueRecurringClusterProtocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException("The execution recurring control did not emit within its original progress window.");
    }

    private static async Task RequireScheduleAsync(RequestCqrsClusterCallers admin,
        RecurringScheduleDefinition definition, bool cancelled, CancellationToken token)
    {
        var request = new InspectRecurringScheduleRequest(definition.Lane, definition.ScheduleId);
        var expected = new RecurringScheduleInspection(definition,
            cancelled ? CancelledRevision : OriginalRevision, Generation, NextOrdinal, cancelled, false, []);
        await QueueLeaseClusterAssertions.EqualAsync(
            await PublicReadAssertions.ScheduleAsync(await admin.Sdk.InspectRecurringScheduleAsync(request, token)), expected);
        var mcp = await PublicReadAssertions.ScheduleAsync(
            await admin.Mcp.CallAsync(McpCallerTools.ScheduleInspect, request, token));
        await QueueLeaseClusterAssertions.EqualAsync(mcp.Value, expected);
        var sql = SqlClusterProtocol.Call(definition.Lane.Partition, McpCallerTools.ScheduleInspect, request);
        await QueueLeaseClusterAssertions.EqualAsync(
            (await SqlClusterProtocol.SdkAsync<RecurringScheduleReadResult>(admin.Sdk, sql, token)).Schedule, expected);
        await QueueLeaseClusterAssertions.EqualAsync(
            (await SqlClusterProtocol.McpAsync<RecurringScheduleReadResult>(admin.Mcp, sql, token)).Schedule, expected);
    }

    private static Task RequireOccurrenceAsync(RequestCqrsClusterCallers admin,
        RecurringScheduleDefinition definition, CancellationToken token)
    {
        var id = DueRecurringClusterProtocol.OccurrenceId(definition.ScheduleId, Generation, FirstOrdinal);
        var expected = new MessageInspection(new(id, MessageState.Ready,
            (int)FirstOrdinal, OriginalRevision, NextOrdinal, definition.FirstDueAt, null), Payload, Headers);
        return QueueDeadlineClusterAssertions.RequireAsync(admin, definition.Lane, expected, token);
    }
}
