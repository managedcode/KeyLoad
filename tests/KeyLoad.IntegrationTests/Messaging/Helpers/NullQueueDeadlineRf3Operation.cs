using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class NullQueueDeadlineRf3Operation
{
    internal static Task<NullQueueDeadlineRf3State> SeedAsync(ClusterFixture fixture, CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var tenant = QueueDeadlineRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
            var partition = new PartitionRef(tenant, QueueDeadlineRf3Protocol.Database, QueueDeadlineRf3Protocol.Domain,
                Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var lane = new QueueLaneRef(partition, QueueDeadlineRf3Protocol.Queue);
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
            var identity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
                [new(partition.DatabaseId, lane.Queue, QueueDeadlineRf3Protocol.Worker)], [], false, token);
            var controlLane = NullQueueDeadlineRf3Observation.ControlLane(lane);
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(tenant, partition.DatabaseId, new(controlLane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
            var controlIdentity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
                [new(partition.DatabaseId, controlLane.Queue,
                    Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect)], [], false, token);
            var now = TimeProvider.System.GetUtcNow();
            var due = now.AddSeconds(QueueDeadlineRf3Protocol.LeadSeconds);
            var control = NullQueueDeadlineRf3Observation.Definition(lane, due);
            var stopControl = new CommandRequest(Guid.NewGuid(), partition,
                [new CancelRecurringSchedule(control.Lane, control.ScheduleId, QueueDeadlineRf3Protocol.First)]);
            var original = new CommandRequest(Guid.NewGuid(), partition,
                [new EnqueueMessage(lane.Queue, QueueDeadlineRf3Protocol.Original, QueueDeadlineRf3Protocol.Payload,
                    QueueDeadlineRf3Protocol.Headers, NotBefore: due),
                 new EnqueueMessage(lane.Queue, QueueDeadlineRf3Protocol.Future, QueueDeadlineRf3Protocol.Payload,
                    QueueDeadlineRf3Protocol.Headers, NotBefore: now.AddHours(QueueDeadlineRf3Protocol.FutureHours))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(original, token));
            var controlCommand = new CommandRequest(Guid.NewGuid(), partition,
                [new ConfigureRecurringSchedule(control, QueueDeadlineRf3Protocol.Initial)]);
            var controlReceipt = await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1,
                controlIdentity.Secret, async scheduler =>
                {
                    var configured = await McpCallerAssertions.SdkSuccessAsync(
                        await scheduler.Sdk.CommitAsync(controlCommand, token));
                    await QueueScheduledClockRf3Replay.EnqueueAsync(scheduler, controlCommand, configured, token);
                    return configured;
                }, token);
            var scheduled = new MessageInspection(new(QueueDeadlineRf3Protocol.Original, MessageState.Scheduled,
                (int)QueueDeadlineRf3Protocol.Initial, QueueDeadlineRf3Protocol.First, QueueDeadlineRf3Protocol.Initial, due, null),
                QueueDeadlineRf3Protocol.Payload, QueueDeadlineRf3Protocol.Headers);
            var future = scheduled with
            {
                Metadata = scheduled.Metadata with
                {
                    Id = QueueDeadlineRf3Protocol.Future,
                    NotBefore = ((EnqueueMessage)original.Mutations[1]).NotBefore
                }
            };
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, scheduled, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, lane, future, token);
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, original, receipt, token);
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BrowseQueueAsync(new(lane), token));
            await Assert.That(page.Counters.StoredMessages).IsEqualTo(QueueDeadlineRf3Protocol.Second);
            await Assert.That(page.Counters.InFlightMessages).IsEqualTo(QueueDeadlineRf3Protocol.Initial);
            await Assert.That(page.Counters.NextReadySequence).IsEqualTo(QueueDeadlineRf3Protocol.Initial);
            await Assert.That(TimeProvider.System.GetUtcNow() < due).IsTrue();
            return new NullQueueDeadlineRf3State(lane, identity, original, receipt, scheduled, future, page,
                control, controlIdentity, controlCommand, controlReceipt, stopControl);
        }, token);

    internal static Task<bool> RequireUnchangedAsync(ClusterFixture fixture, string node,
        NullQueueDeadlineRf3State state, CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, node, fixture.AdminKey, async admin =>
        {
            await QueueDeadlineRf3Assertions.RequireAsync(admin, state.Lane, state.Scheduled, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, state.Lane, state.Future, token);
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, state.Original, state.Receipt, token);
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.BrowseQueueAsync(new(state.Lane), token));
            await QueueLeaseRf3Assertions.EqualAsync(page.Counters, state.OriginalPage.Counters);
            await QueueLeaseRf3Assertions.EqualAsync(page.Items, state.OriginalPage.Items);
            await QueueLeaseRf3Assertions.EqualAsync(page.NextAfterId, state.OriginalPage.NextAfterId);
            return true;
        }, token);

    internal static async Task ContinueAsync(ClusterFixture fixture, NullQueueDeadlineRf3State state,
        CommitReceipt controlCancellation, CancellationToken token)
    {
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await RequireUnchangedAsync(fixture, McpCallerProtocol.Node3, state, token);
        await NullQueueDeadlineRf3Observation.RequireCancelledAsync(fixture, state, controlCancellation, token);
        var ready = state.Scheduled with
        {
            Metadata = state.Scheduled.Metadata with
            {
                State = MessageState.Ready,
                StateVersion = QueueDeadlineRf3Protocol.Second,
                ReadySequence = QueueDeadlineRf3Protocol.First,
                NotBefore = null
            }
        };
        var completed = await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, state.Identity.Secret,
            worker => QueueDeadlineRf3Claim.CompleteAsync(worker, state.Lane, ready, token), token);
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async admin =>
        {
            await QueueScheduledClockRf3Replay.EnqueueAsync(admin, state.Original, state.Receipt, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, state.Lane, completed.Terminal, token);
            await QueueDeadlineRf3Assertions.RequireAsync(admin, state.Lane, state.Future, token);
            return true;
        }, token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node3, state.Identity.Secret, async worker =>
        {
            await QueueScheduledClockRf3Replay.AckAsync(worker, completed.Command, completed.Receipt, token);
            return true;
        }, token);
        await NullQueueDeadlineRf3Observation.RequireCancelledAsync(fixture, state, controlCancellation, token);
        await QueueDeadlineRf3Healthy.RunAsync(fixture, state.Lane, state.Identity, state.Future, token);
    }
}
