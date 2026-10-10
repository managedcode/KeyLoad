using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Continuation
{
    internal static async Task RunAsync(ClusterFixture fixture, QueueLaneRef lane, MessagingRf3Identity identity,
        CommandRequest original, CommitReceipt receipt, MessageInspection scheduled, MessageInspection future,
        CancellationToken token)
    {
        await QueueDeadlineRf3Pause.SetAsync(fixture, lane, true, token);
        await Assert.That(TimeProvider.System.GetUtcNow() < scheduled.Metadata.NotBefore).IsTrue();
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await DueRecurringRf3Assertions.WaitForDueTimeAsync(scheduled.Metadata.NotBefore!.Value, token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async admin =>
        {
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, scheduled, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
        await QueueDeadlineRf3Pause.RefusedAsync(fixture, lane, identity, scheduled, token);
        var revoked = identity.Principal with { Revoked = true, PolicyEpoch = QueueDeadlineRf3Protocol.Second };
        await MessagingRf3Identity.UpdateAsync(fixture, revoked, token);
        await QueueDeadlineRf3Pause.SetAsync(fixture, lane, false, token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, scheduled, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
        var restored = revoked with { Revoked = false, PolicyEpoch = QueueDeadlineRf3Protocol.Third };
        await MessagingRf3Identity.UpdateAsync(fixture, restored, token);
        var ready = scheduled with
        {
            Metadata = scheduled.Metadata with
            {
                State = MessageState.Ready,
                StateVersion = QueueDeadlineRf3Protocol.Second,
                ReadySequence = QueueDeadlineRf3Protocol.First,
                NotBefore = null
            }
        };
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
        {
            await QueueDeadlineRf3Assertions.WaitReadyAsync(admin, lane, ready, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
        var completed = await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, identity.Secret,
            worker => QueueDeadlineRf3Claim.CompleteAsync(worker, lane, ready, token), token);
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async admin =>
        {
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, original, receipt, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, completed.Terminal, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            return true;
        }, token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node3, identity.Secret, async worker =>
        {
            await QueueScheduledClockRf3Replay.AckAsync(worker, completed.Command, completed.Receipt, token);
            return true;
        }, token);
        await QueueDeadlineRf3Healthy.RunAsync(fixture, lane, identity, future, token);
    }
}
