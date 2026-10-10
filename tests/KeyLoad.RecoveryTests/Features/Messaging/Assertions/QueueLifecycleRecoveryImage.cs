using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueLifecycleRecoveryImage
{
    internal static async Task AssertAsync(DatabaseEngine database, ZoneTreeStore store, bool committed)
    {
        var lane = new QueueLaneRef(QueueLifecycleCrashProtocol.Partition, QueueLifecycleCrashProtocol.Queue);
        var first = database.InspectMessage(CrashFixtureValues.Principal, lane, QueueLifecycleCrashProtocol.Parked)!;
        var second = database.InspectMessage(CrashFixtureValues.Principal, lane, QueueLifecycleCrashProtocol.Pending)!;
        var expectedFirst = new MessageMetadata(QueueLifecycleCrashProtocol.Parked,
            committed ? MessageState.Cancelled : MessageState.DeadLettered, QueueLifecycleCrashProtocol.One,
            committed ? QueueLifecycleCrashProtocol.Four : QueueLifecycleCrashProtocol.Three, QueueLifecycleCrashProtocol.One,
            null, null, LeaseVersion: committed ? QueueLifecycleCrashProtocol.Two : QueueLifecycleCrashProtocol.One,
            DeliveryGeneration: committed ? QueueLifecycleCrashProtocol.Two : QueueLifecycleCrashProtocol.One,
            SafeFailureCode: "AttemptsExhausted", ParkedSequence: committed ? QueueLifecycleCrashProtocol.None : QueueLifecycleCrashProtocol.One);
        var expectedSecond = new MessageMetadata(QueueLifecycleCrashProtocol.Pending,
            committed ? MessageState.Ready : MessageState.PendingDeadLetter,
            committed ? QueueLifecycleCrashProtocol.None : QueueLifecycleCrashProtocol.One,
            committed ? QueueLifecycleCrashProtocol.Five : QueueLifecycleCrashProtocol.Three,
            committed ? QueueLifecycleCrashProtocol.Three : QueueLifecycleCrashProtocol.Two, null, null,
            LeaseVersion: committed ? QueueLifecycleCrashProtocol.Two : QueueLifecycleCrashProtocol.One,
            DeliveryGeneration: committed ? QueueLifecycleCrashProtocol.Two : QueueLifecycleCrashProtocol.One,
            SafeFailureCode: committed ? null : "AttemptsExhausted");
        await Assert.That(first.Metadata).IsEqualTo(expectedFirst);
        await Assert.That(second.Metadata).IsEqualTo(expectedSecond);
        await Assert.That(first.PayloadJson).IsEqualTo(committed ? null : QueueLifecycleCrashProtocol.Payload);
        await Assert.That(first.HeadersJson).IsEqualTo(committed ? null : QueueLifecycleCrashProtocol.Headers);
        await Assert.That(second.PayloadJson).IsEqualTo(QueueLifecycleCrashProtocol.Payload);
        await Assert.That(second.HeadersJson).IsEqualTo(QueueLifecycleCrashProtocol.Headers);
        await BodyAsync(store, lane, QueueLifecycleCrashProtocol.Parked, !committed);
        await BodyAsync(store, lane, QueueLifecycleCrashProtocol.Pending, true);
        var expectedCounters = new QueueCounters(committed ? QueueLifecycleCrashProtocol.One : QueueLifecycleCrashProtocol.Two,
            Bytes(QueueLifecycleCrashProtocol.Pending) + (committed ? QueueLifecycleCrashProtocol.None : Bytes(QueueLifecycleCrashProtocol.Parked)),
            QueueLifecycleCrashProtocol.None, QueueLifecycleCrashProtocol.None,
            committed ? QueueLifecycleCrashProtocol.Three : QueueLifecycleCrashProtocol.Two,
            committed ? QueueLifecycleCrashProtocol.None : QueueLifecycleCrashProtocol.One,
            committed ? QueueLifecycleCrashProtocol.None : Bytes(QueueLifecycleCrashProtocol.Parked),
            committed ? QueueLifecycleCrashProtocol.Two : QueueLifecycleCrashProtocol.One);
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition("queue-counters", lane.Partition, lane.Queue))))
            .IsEqualTo(expectedCounters);
    }

    private static long Bytes(string id) => NativeSerialization.Serialize(Literal(id)).LongLength;
    private static MessageBody Literal(string id)
    {
        var message = QueueLifecycleCrashSeed.Enqueue(id);
        return new(id, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message));
    }
    private static async Task BodyAsync(ZoneTreeStore store, QueueLaneRef lane, string id, bool retained)
        => await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition("message-body", lane.Partition, lane.Queue, id))))
            .IsEqualTo(retained ? Literal(id) : null);
}
