using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueDeadlineRecoveryHealthy
{
    internal static async Task RunAsync(DatabaseEngine database)
    {
        var lane = new QueueLaneRef(QueueDeadlineCrashProtocol.Partition, QueueDeadlineCrashProtocol.Queue);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, QueueDeadlineCrashProtocol.Healthy, QueueDeadlineCrashProtocol.Payload,
                QueueDeadlineCrashProtocol.Headers)]);
        CrashDatabase.Submit(database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), lane);
        var result = CrashDatabase.Submit(database, OperationKind.Receive, receive, receive.RequestId).Get<ReceiveResult>();
        var delivery = result.Deliveries.Single();
        await Assert.That(delivery.Id).IsEqualTo(QueueDeadlineCrashProtocol.Healthy);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueDeadlineCrashProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueDeadlineCrashProtocol.Headers);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var receipt = CrashDatabase.Submit(database, OperationKind.Delivery, ack, ack.CommandId).Get<CommitReceipt>();
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt,
            CrashDatabase.Submit(database, OperationKind.Delivery, ack, ack.CommandId).Get<CommitReceipt>());
        var inspection = database.InspectMessage(CrashFixtureValues.Principal, lane, delivery.Id);
        await Assert.That(inspection).IsEqualTo(new MessageInspection(new MessageMetadata(delivery.Id, MessageState.Acked,
            (int)QueueDeadlineCrashProtocol.First, QueueDeadlineCrashProtocol.Third, QueueDeadlineCrashProtocol.Second,
            null, null, LeaseVersion: QueueDeadlineCrashProtocol.First), null, null));
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueDeadlineCrashProtocol.CounterSpace, lane.Partition, lane.Queue)))!;
        await Assert.That(counters.StoredMessages).IsEqualTo(QueueDeadlineCrashProtocol.Initial);
        await Assert.That(counters.StoredBytes).IsEqualTo(QueueDeadlineCrashProtocol.Initial);
        await Assert.That(counters.InFlightMessages).IsEqualTo(QueueDeadlineCrashProtocol.Initial);
        await Assert.That(counters.InFlightBytes).IsEqualTo(QueueDeadlineCrashProtocol.Initial);
    }
}
