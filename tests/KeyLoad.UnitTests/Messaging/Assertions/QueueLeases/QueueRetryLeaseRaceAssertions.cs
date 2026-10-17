using KeyLoad.Core;

using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryLeaseRaceAssertions
{
    internal static async Task TerminalAsync(DatabaseEngine database, QueueOrderedRetryState state, bool renewFirst, bool healthy)
    {
        var ready = renewFirst ? QueueOrderedRetryProtocol.One : QueueOrderedRetryProtocol.Two;
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.First, MessageState.Acked,
            renewFirst ? QueueOrderedRetryProtocol.One : QueueOrderedRetryProtocol.Two,
            renewFirst ? QueueOrderedRetryProtocol.Four : QueueOrderedRetryProtocol.Six, ready, null, null,
            LeaseVersion: renewFirst ? QueueOrderedRetryProtocol.One : QueueOrderedRetryProtocol.Two,
            SafeFailureCode: renewFirst ? null : "RetryRequested", EnqueueSequence: QueueOrderedRetryProtocol.One), stored: false);
        if (healthy)
        {
            ready++;
            await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.Healthy, MessageState.Acked,
                QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, ready, null, null,
                LeaseVersion: QueueOrderedRetryProtocol.One, EnqueueSequence: QueueOrderedRetryProtocol.Two), stored: false);
        }
        var actual = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", state.Partition, state.Lane.Queue)));
        await Assert.That(actual).IsEqualTo(new QueueCounters(QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial,
            QueueOrderedRetryProtocol.Initial, QueueOrderedRetryProtocol.Initial, ready,
            NextOrderSequence: healthy ? QueueOrderedRetryProtocol.Two : QueueOrderedRetryProtocol.One));
        var order = database.Store.Read(view => view.Scan(KeySpace.Partition("queue-order", state.Partition, state.Lane.Queue), QueueOrderedRetryProtocol.One));
        await Assert.That(order.Records).IsEmpty();
        await Assert.That(order.HasMore).IsFalse();
    }
}
