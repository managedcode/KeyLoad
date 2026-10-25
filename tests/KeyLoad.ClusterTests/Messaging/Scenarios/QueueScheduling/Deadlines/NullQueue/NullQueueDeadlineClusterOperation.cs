using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class NullQueueDeadlineClusterOperation
{
    internal static Task<NullQueueDeadlineClusterState> SeedAsync(ClusterFixture fixture, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var tenant = QueueDeadlineClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
            var partition = new PartitionRef(tenant, QueueDeadlineClusterProtocol.Database, QueueDeadlineClusterProtocol.Domain,
                Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var lane = new QueueLaneRef(partition, QueueDeadlineClusterProtocol.Queue);
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
            var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
                [new(partition.DatabaseId, lane.Queue, QueueDeadlineClusterProtocol.Worker)], [], false, token);
            var controlLane = NullQueueDeadlineClusterObservation.ControlLane(lane);
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(controlLane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
            var controlIdentity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
                [new(partition.DatabaseId, controlLane.Queue,
                    Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect)], [], false, token);
            var now = TimeProvider.System.GetUtcNow();
            var due = now.AddSeconds(QueueDeadlineClusterProtocol.LeadSeconds);
            var control = NullQueueDeadlineClusterObservation.Definition(lane, due);
            var stopControl = new CommandRequest(Guid.NewGuid(), partition,
                [new CancelRecurringSchedule(control.Lane, control.ScheduleId, QueueDeadlineClusterProtocol.First)]);
            var original = new CommandRequest(Guid.NewGuid(), partition,
                [new EnqueueMessage(lane.Queue, QueueDeadlineClusterProtocol.Original, QueueDeadlineClusterProtocol.Payload,
                    QueueDeadlineClusterProtocol.Headers, NotBefore: due),
                 new EnqueueMessage(lane.Queue, QueueDeadlineClusterProtocol.Future, QueueDeadlineClusterProtocol.Payload,
                    QueueDeadlineClusterProtocol.Headers, NotBefore: now.AddHours(QueueDeadlineClusterProtocol.FutureHours))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(original, token));
            var controlCommand = new CommandRequest(Guid.NewGuid(), partition,
                [new ConfigureRecurringSchedule(control, QueueDeadlineClusterProtocol.Initial)]);
            var controlReceipt = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1,
                controlIdentity.Secret, async scheduler =>
                {
                    var configured = await McpCallerAssertions.SdkSuccessAsync(
                        await scheduler.Sdk.CommitAsync(controlCommand, token));
                    await QueueScheduledClockClusterReplay.EnqueueAsync(scheduler, controlCommand, configured, token);
                    return configured;
                }, token);
            var scheduled = new MessageInspection(new(QueueDeadlineClusterProtocol.Original, MessageState.Scheduled,
                (int)QueueDeadlineClusterProtocol.Initial, QueueDeadlineClusterProtocol.First, QueueDeadlineClusterProtocol.Initial, due, null),
                QueueDeadlineClusterProtocol.Payload, QueueDeadlineClusterProtocol.Headers);
            var future = scheduled with
            {
                Metadata = scheduled.Metadata with
                {
                    Id = QueueDeadlineClusterProtocol.Future,
                    NotBefore = ((EnqueueMessage)original.Mutations[1]).NotBefore
                }
            };
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, scheduled, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, lane, future, token);
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, original, receipt, token);
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BrowseQueueAsync(new(lane), token));
            await Assert.That(page.Counters.StoredMessages).IsEqualTo(QueueDeadlineClusterProtocol.Second);
            await Assert.That(page.Counters.InFlightMessages).IsEqualTo(QueueDeadlineClusterProtocol.Initial);
            await Assert.That(page.Counters.NextReadySequence).IsEqualTo(QueueDeadlineClusterProtocol.Initial);
            await Assert.That(TimeProvider.System.GetUtcNow() < due).IsTrue();
            return new NullQueueDeadlineClusterState(lane, identity, original, receipt, scheduled, future, page,
                control, controlIdentity, controlCommand, controlReceipt, stopControl);
        }, token);

    internal static Task<bool> RequireUnchangedAsync(ClusterFixture fixture, string node,
        NullQueueDeadlineClusterState state, CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, node, fixture.AdminKey, async admin =>
        {
            await QueueDeadlineClusterAssertions.RequireAsync(admin, state.Lane, state.Scheduled, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, state.Lane, state.Future, token);
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, state.Original, state.Receipt, token);
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BrowseQueueAsync(new(state.Lane), token));
            await QueueLeaseClusterAssertions.EqualAsync(page.Counters, state.OriginalPage.Counters);
            await QueueLeaseClusterAssertions.EqualAsync(page.Items, state.OriginalPage.Items);
            await QueueLeaseClusterAssertions.EqualAsync(page.NextAfterId, state.OriginalPage.NextAfterId);
            return true;
        }, token);

    internal static async Task ContinueAsync(ClusterFixture fixture, NullQueueDeadlineClusterState state,
        CommitReceipt controlCancellation, CancellationToken token)
    {
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await RequireUnchangedAsync(fixture, McpCallerProtocol.Node3, state, token);
        await NullQueueDeadlineClusterObservation.RequireCancelledAsync(fixture, state, controlCancellation, token);
        var ready = state.Scheduled with
        {
            Metadata = state.Scheduled.Metadata with
            {
                State = MessageState.Ready,
                StateVersion = QueueDeadlineClusterProtocol.Second,
                ReadySequence = QueueDeadlineClusterProtocol.First,
                NotBefore = null
            }
        };
        var completed = await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, state.Identity.Secret,
            worker => QueueDeadlineClusterClaim.CompleteAsync(worker, state.Lane, ready, token), token);
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async admin =>
        {
            await QueueScheduledClockClusterReplay.EnqueueAsync(admin, state.Original, state.Receipt, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, state.Lane, completed.Terminal, token);
            await QueueDeadlineClusterAssertions.RequireAsync(admin, state.Lane, state.Future, token);
            return true;
        }, token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, state.Identity.Secret, async worker =>
        {
            await QueueScheduledClockClusterReplay.AckAsync(worker, completed.Command, completed.Receipt, token);
            return true;
        }, token);
        await NullQueueDeadlineClusterObservation.RequireCancelledAsync(fixture, state, controlCancellation, token);
        await QueueDeadlineClusterHealthy.RunAsync(fixture, state.Lane, state.Identity, state.Future, token);
    }
}
