using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueTerminalInspectionAssertions
{
    internal static MessageInspection Expected(MessageState terminalState, QueueLifecycleTestState state)
        => new(terminalState switch
        {
            MessageState.Acked => new(QueueLifecycleConditionOperations.Source, MessageState.Acked,
                1, 3, 1, null, null, LeaseVersion: 1),
            MessageState.Cancelled => new(QueueLifecycleConditionOperations.Source, MessageState.Cancelled,
                0, 2, 1, null, null, LeaseVersion: 1, DeliveryGeneration: 2),
            MessageState.Expired => new(QueueLifecycleConditionOperations.Source, MessageState.Expired,
                0, 2, 1, null, state.Time.AddSeconds(-1)),
            _ => throw new ArgumentOutOfRangeException(nameof(terminalState))
        }, null, null);

    internal static async Task OriginalBodyAsync(byte[] row, MessageState terminalState, QueueLifecycleTestState state)
    {
        var literal = QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Source) with
        { ExpiresAt = terminalState == MessageState.Expired ? state.Time.AddSeconds(-1) : null };
        var expected = new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson,
            literal.OrderingKey, JsonData.Fingerprint(literal));
        await Assert.That(NativeSerialization.Deserialize<MessageBody>(row)).IsEqualTo(expected);
        await Assert.That(row).IsEquivalentTo(NativeSerialization.Serialize(expected), CollectionOrdering.Matching);
    }

    internal static async Task RefusalAsync(DatabaseEngine owner, ZoneTreeStore store,
        QueueLifecycleTestState state, QueueLifecycleConditionCut cut)
    {
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.InspectMessage(
                QueueLifecycleTestProtocol.Administrator, state.Lane, QueueLifecycleConditionOperations.Source));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo("The queue lifecycle body is unavailable.");
            await QueueLifecycleConditionAssertions.Same(store, cut);
        }
    }

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state,
        string? currentMessageId = null)
    {
        var before = QueueLifecycleConditionAssertions.Capture(store);
        foreach (var (operation, result) in state.Successes)
        {
            if (operation.Kind == OperationKind.Receive)
            {
                var current = result.Get<ReceiveResult>().Deliveries.All(item => item.Id == currentMessageId);
                await QueueReceiveReplayAssertions.SameAsync(store, state.Partition, operation, result, current,
                    () => owner.Apply(operation));
            }
            else
            { await NativeReplayResultAssertions.Same<CommitReceipt>(owner.Apply(operation), result); }
            await QueueLifecycleConditionAssertions.Same(store, before);
        }
    }

    internal static async Task DeliveryAsync(Delivery delivery, string id, DateTimeOffset time)
    {
        await Assert.That(delivery.Token).IsNotEmpty();
        await Assert.That(delivery).IsEqualTo(new Delivery(id, QueueLifecycleTestProtocol.Payload,
            QueueLifecycleTestProtocol.Headers, delivery.Token, 1,
            time.AddSeconds(QueueLifecycleTestProtocol.LeaseSeconds), 1, 1));
    }

    internal static async Task TerminalAsync(DatabaseEngine owner, ZoneTreeStore store,
        QueueLifecycleTestState state, MessageInspection expected, bool healthy)
    {
        var before = QueueLifecycleConditionAssertions.Capture(store);
        await Assert.That(owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane,
            QueueLifecycleConditionOperations.Source)).IsEqualTo(expected);
        if (healthy)
        {
            await Assert.That(owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane,
                QueueLifecycleConditionOperations.Healthy)).IsEqualTo(new MessageInspection(
                new(QueueLifecycleConditionOperations.Healthy, MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), null, null));
        }
        var counters = store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueLifecycleTestProtocol.CounterSpace, state.Partition, state.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(0, 0, 0, 0, healthy ? 2 : 1, 0, 0, 0));
        foreach (var family in new[] { QueueLifecycleTestProtocol.BodySpace, QueueLifecycleTestProtocol.ReadySpace,
            QueueLifecycleTestProtocol.ScheduledSpace, QueueLifecycleTestProtocol.LeaseSpace,
            QueueLifecycleTestProtocol.DeadLetterSpace, QueueLifecycleTestProtocol.PendingSpace, QueueLifecycleTestProtocol.ParkedSpace })
        {
            var scan = store.Read(view => view.Scan(KeySpace.Partition(family, state.Partition, state.Lane.Queue), 1));
            await Assert.That(scan.Records).IsEmpty();
            await Assert.That(scan.HasMore).IsFalse();
        }
        await QueueLifecycleConditionAssertions.Same(store, before);
    }
}
