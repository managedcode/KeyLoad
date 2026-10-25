using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDeadlineClusterHealthy
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueLaneRef lane, MessagingClusterIdentity identity,
        MessageInspection future, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, QueueDeadlineClusterProtocol.Healthy,
                QueueDeadlineClusterProtocol.Payload, QueueDeadlineClusterProtocol.Headers)]);
        var ready = new MessageInspection(new(QueueDeadlineClusterProtocol.Healthy, MessageState.Ready,
            (int)QueueDeadlineClusterProtocol.Initial, QueueDeadlineClusterProtocol.First,
            QueueDeadlineClusterProtocol.Second, null, null), QueueDeadlineClusterProtocol.Payload, QueueDeadlineClusterProtocol.Headers);
        var receipt = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, async admin =>
        {
            var committed = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(request, token));
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, request, committed, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, ready, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, future, token);
            return committed;
        }, token);
        var completed = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, worker => QueueDeadlineClusterClaim.CompleteAsync(worker, lane, ready, token), token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
        {
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, request, receipt, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, completed.Terminal, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
    }
}
