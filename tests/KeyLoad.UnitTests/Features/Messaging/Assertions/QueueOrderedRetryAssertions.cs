using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;

using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryAssertions
{
    internal static async Task MessageAsync(DatabaseEngine database, QueueOrderedRetryState state, MessageMetadata expected, bool stored)
    {
        var actual = database.InspectMessage(state.Principal, state.Lane, expected.Id)!;
        await Assert.That(actual.Metadata).IsEqualTo(expected);
        await Assert.That(actual.PayloadJson).IsEqualTo(stored ? QueueOrderedRetryProtocol.Payload : null);
        await Assert.That(actual.HeadersJson).IsEqualTo(stored ? QueueOrderedRetryProtocol.Headers : null);
        var body = database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition("message-body", state.Partition, state.Lane.Queue, expected.Id)));
        var literal = QueueOrderedRetryOperations.Literal(expected.Id,
            expected.Id == QueueOrderedRetryProtocol.Other ? QueueOrderedRetryProtocol.OtherKey : QueueOrderedRetryProtocol.SharedKey);
        await Assert.That(body).IsEqualTo(stored ? new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson,
            literal.OrderingKey, JsonData.Fingerprint(literal)) : null);
    }

    internal static async Task ParkedAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var active = state.ParkedHead == QueueParkedHeadPolicy.Block ? QueueOrderedRetryProtocol.One : QueueOrderedRetryProtocol.Initial;
        await MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.DeadLettered, QueueOrderedRetryProtocol.Two,
            QueueOrderedRetryProtocol.Six, QueueOrderedRetryProtocol.Four, null, null, LeaseVersion: QueueOrderedRetryProtocol.Two,
            SafeFailureCode: "AttemptsExhausted", ParkedSequence: QueueOrderedRetryProtocol.One,
            EnqueueSequence: QueueOrderedRetryProtocol.One, ActiveOrderSequence: active), stored: true);
    }

    internal static async Task RedrivenAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var active = state.ParkedHead == QueueParkedHeadPolicy.Block ? QueueOrderedRetryProtocol.One : QueueOrderedRetryProtocol.Four;
        await MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.Ready, QueueOrderedRetryProtocol.Initial,
            QueueOrderedRetryProtocol.Seven, QueueOrderedRetryProtocol.Five, null, null, LeaseVersion: QueueOrderedRetryProtocol.Three,
            DeliveryGeneration: QueueOrderedRetryProtocol.Two, EnqueueSequence: QueueOrderedRetryProtocol.One, ActiveOrderSequence: active), stored: true);
        var actual = database.Store.Read(view => view.GetRecord<QueueOrderReference>(KeySpace.Partition("queue-order", state.Partition,
            state.Lane.Queue, QueueOrderedRetryProtocol.SharedKey, active, QueueOrderedRetryProtocol.First)));
        await Assert.That(actual).IsEqualTo(new QueueOrderReference(QueueOrderedRetryProtocol.First, active, QueueOrderedRetryProtocol.Two));
    }

    internal static async Task TerminalAsync(DatabaseEngine database, QueueOrderedRetryState state, bool healthy)
    {
        await MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.Acked, QueueOrderedRetryProtocol.One,
            QueueOrderedRetryProtocol.Nine, QueueOrderedRetryProtocol.Five, null, null, LeaseVersion: QueueOrderedRetryProtocol.Four,
            DeliveryGeneration: QueueOrderedRetryProtocol.Two, EnqueueSequence: QueueOrderedRetryProtocol.One), stored: false);
        await AckedAsync(database, state, QueueOrderedRetryProtocol.Second, QueueOrderedRetryProtocol.Two, QueueOrderedRetryProtocol.Two);
        await AckedAsync(database, state, QueueOrderedRetryProtocol.Other, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.Three);
        var order = state.ParkedHead == QueueParkedHeadPolicy.Block ? QueueOrderedRetryProtocol.Three : QueueOrderedRetryProtocol.Four;
        if (healthy)
        { await AckedAsync(database, state, QueueOrderedRetryProtocol.Healthy, QueueOrderedRetryProtocol.Six, ++order); }
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", state.Partition, state.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial,
            QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial, healthy ? QueueOrderedRetryProtocol.Six : QueueOrderedRetryProtocol.Five,
            NextParkedSequence: QueueOrderedRetryProtocol.One, NextOrderSequence: order));
        var remaining = database.Store.Read(view => view.Scan(KeySpace.Partition("queue-order", state.Partition, state.Lane.Queue), QueueOrderedRetryProtocol.One));
        await Assert.That(remaining.Records).IsEmpty();
        await Assert.That(remaining.HasMore).IsFalse();
    }

    private static Task AckedAsync(DatabaseEngine database, QueueOrderedRetryState state, string id, long ready, long enqueue)
        => MessageAsync(database, state, new(id, MessageState.Acked, QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three,
            ready, null, null, LeaseVersion: QueueOrderedRetryProtocol.One, EnqueueSequence: enqueue), stored: false);
}
