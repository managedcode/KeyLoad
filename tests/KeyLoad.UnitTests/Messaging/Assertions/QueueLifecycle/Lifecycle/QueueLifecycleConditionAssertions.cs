using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleConditionAssertions
{
    internal static QueueLifecycleConditionCut Capture(ZoneTreeStore store)
        => new(store.Position, NativeSerialization.Serialize(store.Identity), QueueWholeFlowStorage.Bytes(store));
    internal static async Task Same(ZoneTreeStore store, QueueLifecycleConditionCut expected)
    {
        var actual = Capture(store);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Identity.SequenceEqual(expected.Identity)).IsTrue();
        await Assert.That(actual.Image).IsEquivalentTo(expected.Image, CollectionOrdering.Matching);
    }
    internal static async Task Corrupt(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state, Command<DatabaseCommand> operation)
    {
        var before = Capture(store);
        await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Partition, QueueLifecycleTestProtocol.Administrator, operation.CommandId)))).IsNull();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(operation));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Partition, QueueLifecycleTestProtocol.Administrator, operation.CommandId)))).IsNull();
        await Same(store, before);
    }
    internal static async Task Replay(DatabaseEngine owner, ZoneTreeStore store, Command<DatabaseCommand> operation, Result<object> original)
    {
        var before = Capture(store);
        var replay = owner.Apply(operation);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await Same(store, before);
    }
    internal static async Task Ready(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var before = Capture(store);
        var literal = QueueLifecycleHealthy.Enqueue(QueueLifecycleConditionOperations.Source);
        var expected = new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson,
            literal.OrderingKey, JsonData.Fingerprint(literal));
        var inspected = owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, literal.MessageId)!;
        await Assert.That(inspected.Metadata).IsEqualTo(new MessageMetadata(literal.MessageId, MessageState.Ready, 0, 1, 1, null, null));
        await Assert.That(inspected.PayloadJson).IsEqualTo(literal.PayloadJson);
        await Assert.That(inspected.HeadersJson).IsEqualTo(literal.HeadersJson);
        var body = store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
            QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, literal.MessageId)));
        await Assert.That(body).IsEqualTo(expected);
        var counters = store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueLifecycleTestProtocol.CounterSpace, state.Partition, state.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(1, NativeSerialization.Serialize(expected).LongLength, 0, 0, 1));
        await Same(store, before);
    }
    internal static async Task Terminal(DatabaseEngine owner, ZoneTreeStore store, QueueLifecycleTestState state)
    {
        var before = Capture(store);
        await Message(owner, state, QueueLifecycleConditionOperations.Source,
            new(QueueLifecycleConditionOperations.Source, MessageState.Cancelled, 0, 2, 1, null, null, LeaseVersion: 1, DeliveryGeneration: 2));
        await Message(owner, state, QueueLifecycleConditionOperations.Healthy,
            new(QueueLifecycleConditionOperations.Healthy, MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1));
        var counters = store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueLifecycleTestProtocol.CounterSpace, state.Partition, state.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(0, 0, 0, 0, 2, 0, 0, 0));
        foreach (var family in new[] { QueueLifecycleTestProtocol.BodySpace, QueueLifecycleTestProtocol.ReadySpace,
            QueueLifecycleTestProtocol.ScheduledSpace, QueueLifecycleTestProtocol.LeaseSpace,
            QueueLifecycleTestProtocol.DeadLetterSpace, QueueLifecycleTestProtocol.PendingSpace, QueueLifecycleTestProtocol.ParkedSpace })
        {
            var scan = store.Read(view => view.Scan(KeySpace.Partition(family, state.Partition, state.Lane.Queue), 1));
            await Assert.That(scan.Records).IsEmpty();
            await Assert.That(scan.HasMore).IsFalse();
        }
        await Same(store, before);
    }
    private static async Task Message(DatabaseEngine owner, QueueLifecycleTestState state, string id, MessageMetadata expected)
    {
        var actual = owner.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, id)!;
        await Assert.That(actual.Metadata).IsEqualTo(expected);
        await Assert.That(actual.PayloadJson).IsNull();
        await Assert.That(actual.HeadersJson).IsNull();
    }
}
