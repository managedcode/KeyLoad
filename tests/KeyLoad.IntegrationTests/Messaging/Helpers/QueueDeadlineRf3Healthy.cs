using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Healthy
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueLaneRef lane, MessagingRf3Identity identity,
        MessageInspection future, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, QueueDeadlineRf3Protocol.Healthy,
                QueueDeadlineRf3Protocol.Payload, QueueDeadlineRf3Protocol.Headers)]);
        var ready = new MessageInspection(new(QueueDeadlineRf3Protocol.Healthy, MessageState.Ready,
            (int)QueueDeadlineRf3Protocol.Initial, QueueDeadlineRf3Protocol.First,
            QueueDeadlineRf3Protocol.Second, null, null), QueueDeadlineRf3Protocol.Payload, QueueDeadlineRf3Protocol.Headers);
        var receipt = await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, async admin =>
        {
            var committed = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(request, token));
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, request, committed, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, ready, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            return committed;
        }, token);
        var completed = await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, worker => QueueDeadlineRf3Claim.CompleteAsync(worker, lane, ready, token), token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
        {
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, request, receipt, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, completed.Terminal, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
    }
}
