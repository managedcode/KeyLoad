using KeyLoad.Core;

using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryRestoredAssertions
{
    internal static async Task TerminalAsync(DatabaseEngine database, QueueOrderedRetryState state, bool healthy)
    {
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.Acked,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Nine, QueueOrderedRetryProtocol.Five, null, null,
            LeaseVersion: QueueOrderedRetryProtocol.Four, DeliveryGeneration: QueueOrderedRetryProtocol.Two,
            EnqueueSequence: QueueOrderedRetryProtocol.One), stored: false);
        var continued = state.ParkedHead == QueueParkedHeadPolicy.Continue;
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.Second, MessageState.Cancelled,
            continued ? QueueOrderedRetryProtocol.One : QueueOrderedRetryProtocol.Initial,
            continued ? QueueOrderedRetryProtocol.Three : QueueOrderedRetryProtocol.Two, QueueOrderedRetryProtocol.Two, null, null,
            LeaseVersion: continued ? QueueOrderedRetryProtocol.Two : QueueOrderedRetryProtocol.One,
            DeliveryGeneration: QueueOrderedRetryProtocol.Two, EnqueueSequence: QueueOrderedRetryProtocol.Two), stored: false);
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.Other, MessageState.Acked,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.Three, null, null,
            LeaseVersion: QueueOrderedRetryProtocol.One, EnqueueSequence: QueueOrderedRetryProtocol.Three), stored: false);
        var order = continued ? QueueOrderedRetryProtocol.Four : QueueOrderedRetryProtocol.Three;
        if (healthy)
        {
            await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.Healthy, MessageState.Acked,
                QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.Six, null, null,
                LeaseVersion: QueueOrderedRetryProtocol.One, EnqueueSequence: ++order), stored: false);
        }
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", state.Partition, state.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial,
            QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial, healthy ? QueueOrderedRetryProtocol.Six : QueueOrderedRetryProtocol.Five,
            NextParkedSequence: QueueOrderedRetryProtocol.One, NextOrderSequence: order));
        var remaining = database.Store.Read(view => view.Scan(KeySpace.Partition("queue-order", state.Partition, state.Lane.Queue), QueueOrderedRetryProtocol.One));
        await Assert.That(remaining.Records).IsEmpty();
        await Assert.That(remaining.HasMore).IsFalse();
    }
}
