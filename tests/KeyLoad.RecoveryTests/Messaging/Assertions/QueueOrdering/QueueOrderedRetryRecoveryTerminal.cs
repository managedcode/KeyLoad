using KeyLoad.Core;
using KeyLoad.CrashHost;

using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueOrderedRetryRecoveryTerminal
{
    internal static async Task AssertAsync(DatabaseEngine database, bool healthy)
    {
        var lane = new QueueLaneRef(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue);
        await QueueOrderedRetryRecoveryImage.MessageAsync(database, lane, new(QueueOrderedRetryCrashProtocol.First, MessageState.Acked,
            QueueOrderedRetryCrashProtocol.Two, QueueOrderedRetryCrashProtocol.Six, QueueOrderedRetryCrashProtocol.Three, null, null,
            LeaseVersion: QueueOrderedRetryCrashProtocol.Two, SafeFailureCode: "RetryRequested", EnqueueSequence: QueueOrderedRetryCrashProtocol.One), retained: false);
        await QueueOrderedRetryRecoveryImage.MessageAsync(database, lane, new(QueueOrderedRetryCrashProtocol.Second, MessageState.Acked,
            QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.Three, QueueOrderedRetryCrashProtocol.Two, null, null,
            LeaseVersion: QueueOrderedRetryCrashProtocol.One, EnqueueSequence: QueueOrderedRetryCrashProtocol.Two), retained: false);
        if (healthy)
        {
            await QueueOrderedRetryRecoveryImage.MessageAsync(database, lane, new(QueueOrderedRetryCrashProtocol.Healthy, MessageState.Acked,
                QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.Three, QueueOrderedRetryCrashProtocol.Four, null, null,
                LeaseVersion: QueueOrderedRetryCrashProtocol.One, EnqueueSequence: QueueOrderedRetryCrashProtocol.Three), retained: false);
        }
        await Assert.That(database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", lane.Partition, lane.Queue))))
            .IsEqualTo(new QueueCounters(QueueOrderedRetryCrashProtocol.Initial, QueueOrderedRetryCrashProtocol.Initial,
                QueueOrderedRetryCrashProtocol.Initial, QueueOrderedRetryCrashProtocol.Initial,
                healthy ? QueueOrderedRetryCrashProtocol.Four : QueueOrderedRetryCrashProtocol.Three,
                NextOrderSequence: healthy ? QueueOrderedRetryCrashProtocol.Three : QueueOrderedRetryCrashProtocol.Two));
        var order = database.Store.Read(view => view.Scan(KeySpace.Partition("queue-order", lane.Partition, lane.Queue), QueueOrderedRetryCrashProtocol.One));
        await Assert.That(order.Records).IsEmpty();
        await Assert.That(order.HasMore).IsFalse();
    }
}
