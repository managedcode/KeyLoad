using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDeadlineClusterContinuation
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueLaneRef lane, MessagingClusterIdentity identity,
        CommandRequest original, CommitReceipt receipt, MessageInspection scheduled, MessageInspection future,
        CancellationToken token)
    {
        await QueueDeadlineClusterPause.SetAsync(fixture, lane, true, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < scheduled.Metadata.NotBefore).IsTrue();
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await DueRecurringClusterAssertions.WaitForDueTimeAsync(scheduled.Metadata.NotBefore!.Value, token);
        await RequirePreservedAsync(fixture, McpCallerProtocol.Node2, lane, scheduled, future, token);
        await QueueDeadlineClusterPause.RefusedAsync(fixture, lane, identity, scheduled, token);
        var revoked = identity.Principal with
        {
            Revoked = true,
            PolicyEpoch = QueueDeadlineClusterProtocol.Second
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, revoked, token);
        await QueueDeadlineClusterPause.SetAsync(fixture, lane, false, token);
        await RequirePreservedAsync(fixture, McpCallerProtocol.Node1, lane, scheduled, future, token);
        var restored = revoked with
        {
            Revoked = false,
            PolicyEpoch = QueueDeadlineClusterProtocol.Third
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, restored, token);
        var ready = scheduled with
        {
            Metadata = scheduled.Metadata with
            {
                State = MessageState.Ready,
                StateVersion = QueueDeadlineClusterProtocol.Second,
                ReadySequence = QueueDeadlineClusterProtocol.First,
                NotBefore = null
            }
        };
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
        {
            await QueueDeadlineClusterAssertions.WaitReadyAsync(admin, lane, ready, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
        var completed = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, identity.Secret,
            worker => QueueDeadlineClusterClaim.CompleteAsync(worker, lane, ready, token), token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async admin =>
        {
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, original, receipt, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, completed.Terminal, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, identity.Secret, async worker =>
        {
            await QueueScheduledClockClusterReplay.AckAsync(worker, completed.Command, completed.Receipt, token);
            return true;
        }, token);
        await QueueDeadlineClusterHealthy.RunAsync(fixture, lane, identity, future, token);
    }

    private static Task<bool> RequirePreservedAsync(ClusterFixture fixture, string node, QueueLaneRef lane,
        MessageInspection scheduled, MessageInspection future, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, node, fixture.AdminKey, async admin =>
        {
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, scheduled, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
}
