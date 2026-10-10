using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueDeadlineRecoveryImage
{
    internal static async Task RequireAsync(DatabaseEngine database, bool committed, DateTimeOffset expiry)
    {
        var lane = new QueueLaneRef(QueueDeadlineCrashProtocol.Partition, QueueDeadlineCrashProtocol.Queue);
        var actual = database.InspectMessage(CrashFixtureValues.Principal, lane, QueueDeadlineCrashProtocol.Message);
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Metadata.State).IsEqualTo(committed ? MessageState.Expired : MessageState.Ready);
        await Assert.That(actual.Metadata.StateVersion).IsEqualTo(committed ? QueueDeadlineCrashProtocol.Second : QueueDeadlineCrashProtocol.First);
        await Assert.That(actual.Metadata.ReadySequence).IsEqualTo(QueueDeadlineCrashProtocol.First);
        await Assert.That(actual.Metadata.Attempts).IsEqualTo((int)QueueDeadlineCrashProtocol.Initial);
        await Assert.That(actual.Metadata.LeaseVersion).IsEqualTo(QueueDeadlineCrashProtocol.Initial);
        await Assert.That(actual.Metadata.LeaseOwner).IsNull();
        await Assert.That(actual.Metadata.LeaseUntil).IsNull();
        await Assert.That(actual.Metadata.NotBefore).IsNull();
        await Assert.That(actual.PayloadJson).IsEqualTo(committed ? null : QueueDeadlineCrashProtocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(committed ? null : QueueDeadlineCrashProtocol.Headers);
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueDeadlineCrashProtocol.CounterSpace, lane.Partition, lane.Queue)))!;
        var message = new EnqueueMessage(lane.Queue, QueueDeadlineCrashProtocol.Message,
            QueueDeadlineCrashProtocol.Payload, QueueDeadlineCrashProtocol.Headers, ExpiresAt: expiry);
        var expectedBody = new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson,
            message.OrderingKey, JsonData.Fingerprint(message));
        await Assert.That(database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
            QueueDeadlineCrashProtocol.BodySpace, lane.Partition, lane.Queue, message.MessageId))))
            .IsEqualTo(committed ? null : expectedBody);
        await Assert.That(actual.Metadata).IsEqualTo(new MessageMetadata(message.MessageId,
            committed ? MessageState.Expired : MessageState.Ready, (int)QueueDeadlineCrashProtocol.Initial,
            committed ? QueueDeadlineCrashProtocol.Second : QueueDeadlineCrashProtocol.First,
            QueueDeadlineCrashProtocol.First, null, expiry));
        await Assert.That(counters).IsEqualTo(new QueueCounters(
            committed ? QueueDeadlineCrashProtocol.Initial : QueueDeadlineCrashProtocol.First,
            committed ? QueueDeadlineCrashProtocol.Initial : NativeSerialization.Serialize(expectedBody).LongLength,
            QueueDeadlineCrashProtocol.Initial, QueueDeadlineCrashProtocol.Initial, QueueDeadlineCrashProtocol.First));
        await Assert.That(counters.StoredMessages).IsEqualTo(committed ? QueueDeadlineCrashProtocol.Initial : QueueDeadlineCrashProtocol.First);
        await Assert.That(counters.InFlightMessages).IsEqualTo(QueueDeadlineCrashProtocol.Initial);
        await Assert.That(counters.InFlightBytes).IsEqualTo(QueueDeadlineCrashProtocol.Initial);
        await Assert.That(counters.NextReadySequence).IsEqualTo(QueueDeadlineCrashProtocol.First);

    }
}
