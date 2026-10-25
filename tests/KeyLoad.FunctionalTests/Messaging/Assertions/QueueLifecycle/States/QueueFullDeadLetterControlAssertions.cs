using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueFullDeadLetterControlAssertions
{
    private const string Exhausted = "AttemptsExhausted";

    internal static async Task PendingAsync(DatabaseEngine database, QueueLifecycleTestState state)
    {
        await MessageAsync(database, state, new(QueueLifecycleTestProtocol.Held, MessageState.PendingDeadLetter,
            QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.Three, null, null,
            LeaseVersion: QueueLifecycleTestProtocol.One, SafeFailureCode: Exhausted), stored: true);
        var reference = database.Store.Read(view => view.GetRecord<QueuePendingDeadLetterReference>(KeySpace.Partition(
            QueueLifecycleTestProtocol.PendingSpace, state.Partition, state.Lane.Queue, QueueLifecycleTestProtocol.Held)));
        await Assert.That(reference).IsEqualTo(new QueuePendingDeadLetterReference(QueueLifecycleTestProtocol.Held,
            QueueLifecycleTestProtocol.One, Exhausted));
        var bytes = QueueLifecycleAccountingAssertions.BodyBytes(QueueLifecycleTestProtocol.Parked)
            + QueueLifecycleAccountingAssertions.BodyBytes(QueueLifecycleTestProtocol.Pending)
            + QueueLifecycleAccountingAssertions.BodyBytes(QueueLifecycleTestProtocol.Held);
        await Assert.That(QueueLifecycleControlCold.Read(database, state.Lane)).IsEqualTo(new QueueCounters(
            QueueLifecycleTestProtocol.Three, bytes, QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.None,
            QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.One,
            QueueLifecycleAccountingAssertions.BodyBytes(QueueLifecycleTestProtocol.Parked), long.MaxValue));
    }

    internal static async Task TerminalAsync(DatabaseEngine database, QueueLifecycleTestState state,
        long expectedParkedSequence = QueueLifecycleTestProtocol.Two)
    {
        await MessageAsync(database, state, new(QueueLifecycleTestProtocol.Parked, MessageState.Cancelled,
            QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.One, null, null,
            LeaseVersion: QueueLifecycleTestProtocol.Two, DeliveryGeneration: QueueLifecycleTestProtocol.Two,
            SafeFailureCode: Exhausted), stored: false);
        await MessageAsync(database, state, new(QueueLifecycleTestProtocol.Pending, MessageState.Acked,
            QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Seven, QueueLifecycleTestProtocol.Four, null, null,
            LeaseVersion: QueueLifecycleTestProtocol.Three, DeliveryGeneration: QueueLifecycleTestProtocol.Two), stored: false);
        await MessageAsync(database, state, new(QueueLifecycleTestProtocol.Held, MessageState.Cancelled,
            QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Four, QueueLifecycleTestProtocol.Three, null, null,
            LeaseVersion: QueueLifecycleTestProtocol.Two, DeliveryGeneration: QueueLifecycleTestProtocol.Two,
            SafeFailureCode: Exhausted), stored: false);
        await MessageAsync(database, state, new(QueueLifecycleTestProtocol.Healthy, MessageState.Acked,
            QueueLifecycleTestProtocol.One, QueueLifecycleTestProtocol.Three, QueueLifecycleTestProtocol.Five, null, null,
            LeaseVersion: QueueLifecycleTestProtocol.One), stored: false);
        await Assert.That(QueueLifecycleControlCold.Read(database, state.Lane)).IsEqualTo(new QueueCounters(
            QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.None,
            QueueLifecycleTestProtocol.None, QueueLifecycleTestProtocol.Five, NextParkedSequence: expectedParkedSequence));
        await QueueLifecycleCeilingTerminalIndices.EmptyAsync((ZoneTreeStore)database.Store, state.Lane);
    }

    private static async Task MessageAsync(DatabaseEngine database, QueueLifecycleTestState state,
        MessageMetadata expected, bool stored)
    {
        var actual = database.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, expected.Id)!;
        await Assert.That(actual.Metadata).IsEqualTo(expected);
        await Assert.That(actual.PayloadJson).IsEqualTo(stored ? QueueLifecycleTestProtocol.Payload : null);
        await Assert.That(actual.HeadersJson).IsEqualTo(stored ? QueueLifecycleTestProtocol.Headers : null);
        var body = database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
            QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, expected.Id)));
        var literal = QueueLifecycleHealthy.Enqueue(expected.Id);
        await Assert.That(body).IsEqualTo(stored ? new MessageBody(literal.MessageId, literal.PayloadJson,
            literal.HeadersJson, literal.OrderingKey, JsonData.Fingerprint(literal)) : null);
    }
}
