using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueStrictHeadBodyAssertions
{
    internal static async Task RefusalAsync(DatabaseEngine owner, ZoneTreeStore store, QueueStrictHeadBodyState state)
    {
        var queue = state.Queue;
        var key = KeySpace.PartitionOutcome(queue.Partition, queue.Principal, state.BlockedReceive.Id);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(key))).IsNull();
            var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(state.BlockedReceive));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.RecoveryRequired);
            await Assert.That(error.Message).IsEqualTo("The strict queue order authority does not match its message.");
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(key))).IsNull();
            await QueueLifecycleConditionAssertions.Same(store, state.Damaged);
        }
    }

    internal static async Task DeliveryAsync(Delivery delivery, string id, DateTimeOffset time)
    {
        await Assert.That(delivery.Token).IsNotEmpty();
        await Assert.That(delivery).IsEqualTo(new Delivery(id, QueueOrderedRetryProtocol.Payload,
            QueueOrderedRetryProtocol.Headers, delivery.Token, 1, time.AddSeconds(30), 1, 1));
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, QueueOrderedRetryState queue,
        string? currentMessageId = null)
    {
        var before = QueueLifecycleConditionAssertions.Capture(store);
        foreach (var (operation, result) in queue.Outcomes)
        {
            if (operation.Kind == OperationKind.Receive)
            {
                var current = result.Get<ReceiveResult>().Deliveries.All(item => item.Id == currentMessageId);
                await QueueReceiveReplayAssertions.SameAsync(store, queue.Partition, operation, result, current,
                    () => owner.Apply(operation));
            }
            else if (operation.Kind is OperationKind.Batch or OperationKind.Delivery)
            { await NativeReplayResultAssertions.Same<CommitReceipt>(owner.Apply(operation), result); }
            else
            { throw new InvalidOperationException("The strict-head fixture requires its original typed operation receipt."); }
            await QueueLifecycleConditionAssertions.Same(store, before);
        }
    }

    internal static async Task HeldAsync(DatabaseEngine owner, ZoneTreeStore store, QueueStrictHeadBodyState state)
    {
        var queue = state.Queue;
        var before = QueueLifecycleConditionAssertions.Capture(store);
        var scheduled = state.HeadState == MessageState.Scheduled;
        var expected = new MessageMetadata(QueueOrderedRetryProtocol.First, state.HeadState,
            scheduled ? 0 : 1, scheduled ? 1 : 2, scheduled ? 0 : 1,
            scheduled ? state.ScheduledAt : null, null, LeaseOwner: scheduled ? null : queue.Principal,
            LeaseVersion: scheduled ? 0 : 1, LeaseUntil: scheduled ? null : queue.Time.AddSeconds(30),
            EnqueueSequence: 1, ActiveOrderSequence: 1);
        await Assert.That(owner.InspectMessage(queue.Principal, queue.Lane, expected.Id)).IsEqualTo(
            new MessageInspection(expected, QueueOrderedRetryProtocol.Payload, QueueOrderedRetryProtocol.Headers));
        var literal = QueueOrderedRetryOperations.Literal(expected.Id) with
        { NotBefore = scheduled ? state.ScheduledAt : null };
        var body = store.Read(view => view.GetRecord<MessageBody>(state.BodyKey));
        await Assert.That(body).IsEqualTo(new MessageBody(literal.MessageId, literal.PayloadJson,
            literal.HeadersJson, literal.OrderingKey, JsonData.Fingerprint(literal)));
        await FollowerAsync(owner, store, state);
        await QueueLifecycleConditionAssertions.Same(store, before);
    }

    internal static async Task FollowerAsync(DatabaseEngine owner, ZoneTreeStore store, QueueStrictHeadBodyState state)
    {
        var before = QueueLifecycleConditionAssertions.Capture(store);
        await QueueOrderedRetryAssertions.MessageAsync(owner, state.Queue, new(QueueOrderedRetryProtocol.Second,
            MessageState.Ready, 0, 1, state.HeadState == MessageState.Scheduled ? 1 : 2, null, null,
            EnqueueSequence: 2, ActiveOrderSequence: 2), stored: true);
        await QueueLifecycleConditionAssertions.Same(store, before);
    }

    internal static async Task TerminalAsync(DatabaseEngine owner, ZoneTreeStore store, QueueStrictHeadBodyState state)
    {
        var queue = state.Queue;
        var before = QueueLifecycleConditionAssertions.Capture(store);
        var scheduled = state.HeadState == MessageState.Scheduled;
        await TerminalMessageAsync(owner, queue, new(QueueOrderedRetryProtocol.First, MessageState.Acked,
            1, scheduled ? 4 : 3, scheduled ? 3 : 1, null, null, LeaseVersion: 1, EnqueueSequence: 1));
        await TerminalMessageAsync(owner, queue, new(QueueOrderedRetryProtocol.Second, MessageState.Acked,
            1, 3, scheduled ? 1 : 2, null, null, LeaseVersion: 1, EnqueueSequence: 2));
        await TerminalMessageAsync(owner, queue, new(QueueOrderedRetryProtocol.Other, MessageState.Acked,
            1, 3, scheduled ? 2 : 3, null, null, LeaseVersion: 1, EnqueueSequence: 3));
        await TerminalMessageAsync(owner, queue, new(QueueOrderedRetryProtocol.Healthy, MessageState.Acked,
            1, 3, 4, null, null, LeaseVersion: 1, EnqueueSequence: 4));
        var counters = store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            "queue-counters", queue.Partition, queue.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(0, 0, 0, 0, 4, NextOrderSequence: 4));
        foreach (var family in new[] { "ready", "scheduled", "lease", "message-body", "dead-letter",
            "queue-pending-dead-letter", "queue-dead-letter-order", "queue-order" })
        {
            var scan = store.Read(view => view.Scan(KeySpace.Partition(family, queue.Partition, queue.Lane.Queue), 1));
            await Assert.That(scan.Records).IsEmpty();
            await Assert.That(scan.HasMore).IsFalse();
        }
        await QueueLifecycleConditionAssertions.Same(store, before);
    }

    private static async Task TerminalMessageAsync(DatabaseEngine owner, QueueOrderedRetryState queue, MessageMetadata expected)
        => await Assert.That(owner.InspectMessage(queue.Principal, queue.Lane, expected.Id))
            .IsEqualTo(new MessageInspection(expected, null, null));
}
