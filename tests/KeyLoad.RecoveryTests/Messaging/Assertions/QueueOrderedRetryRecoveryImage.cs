using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.CrashHost;

using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueOrderedRetryRecoveryImage
{
    internal static async Task AssertAsync(DatabaseEngine database, Delivery original, QueueRetryDecision retry, bool committed)
    {
        var lane = new QueueLaneRef(QueueOrderedRetryCrashProtocol.Partition, QueueOrderedRetryCrashProtocol.Queue);
        await MessageAsync(database, lane, new(QueueOrderedRetryCrashProtocol.First,
            committed ? MessageState.Scheduled : MessageState.Leased, QueueOrderedRetryCrashProtocol.One,
            committed ? QueueOrderedRetryCrashProtocol.Three : QueueOrderedRetryCrashProtocol.Two, QueueOrderedRetryCrashProtocol.One,
            committed ? retry.RetryAt : null, null, committed ? null : CrashFixtureValues.Principal,
            LeaseVersion: QueueOrderedRetryCrashProtocol.One, LeaseUntil: committed ? null : original.LeaseUntil,
            SafeFailureCode: committed ? "RetryRequested" : null, EnqueueSequence: QueueOrderedRetryCrashProtocol.One,
            ActiveOrderSequence: QueueOrderedRetryCrashProtocol.One), retained: true);
        await MessageAsync(database, lane, new(QueueOrderedRetryCrashProtocol.Second, MessageState.Ready, QueueOrderedRetryCrashProtocol.Initial,
            QueueOrderedRetryCrashProtocol.One, QueueOrderedRetryCrashProtocol.Two, null, null,
            EnqueueSequence: QueueOrderedRetryCrashProtocol.Two, ActiveOrderSequence: QueueOrderedRetryCrashProtocol.Two), retained: true);
        var expected = new QueueCounters(QueueOrderedRetryCrashProtocol.Two, Bytes(QueueOrderedRetryCrashProtocol.First) + Bytes(QueueOrderedRetryCrashProtocol.Second),
            committed ? QueueOrderedRetryCrashProtocol.Initial : QueueOrderedRetryCrashProtocol.One,
            committed ? QueueOrderedRetryCrashProtocol.Initial : Bytes(QueueOrderedRetryCrashProtocol.First),
            QueueOrderedRetryCrashProtocol.Two, NextOrderSequence: QueueOrderedRetryCrashProtocol.Two);
        await Assert.That(database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", lane.Partition, lane.Queue))))
            .IsEqualTo(expected);
        foreach (var (id, sequence) in new[] { (QueueOrderedRetryCrashProtocol.First, QueueOrderedRetryCrashProtocol.One),
            (QueueOrderedRetryCrashProtocol.Second, QueueOrderedRetryCrashProtocol.Two) })
        {
            await Assert.That(database.Store.Read(view => view.GetRecord<QueueOrderReference>(KeySpace.Partition("queue-order", lane.Partition,
                lane.Queue, QueueOrderedRetryCrashProtocol.Key, sequence, id))))
                .IsEqualTo(new QueueOrderReference(id, sequence, QueueOrderedRetryCrashProtocol.One));
        }
        await Assert.That(database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition("lease", lane.Partition, lane.Queue,
            original.LeaseUntil, original.Id)))).IsEqualTo(committed ? null : original.Id);
        await Assert.That(database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition("scheduled", lane.Partition, lane.Queue,
            retry.RetryAt, original.Id)))).IsEqualTo(committed ? original.Id : null);
    }

    internal static async Task MessageAsync(DatabaseEngine database, QueueLaneRef lane, MessageMetadata expected, bool retained)
    {
        var actual = database.InspectMessage(CrashFixtureValues.Principal, lane, expected.Id)!;
        await Assert.That(actual.Metadata).IsEqualTo(expected);
        await Assert.That(actual.PayloadJson).IsEqualTo(retained ? QueueOrderedRetryCrashProtocol.Payload : null);
        await Assert.That(actual.HeadersJson).IsEqualTo(retained ? QueueOrderedRetryCrashProtocol.Headers : null);
        await Assert.That(database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition("message-body", lane.Partition, lane.Queue, expected.Id))))
            .IsEqualTo(retained ? Literal(expected.Id) : null);
    }
    private static long Bytes(string id) => NativeSerialization.Serialize(Literal(id)).LongLength;
    private static MessageBody Literal(string id)
    {
        var message = QueueOrderedRetryCrashSeed.Enqueue(id);
        return new(id, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message));
    }
}
