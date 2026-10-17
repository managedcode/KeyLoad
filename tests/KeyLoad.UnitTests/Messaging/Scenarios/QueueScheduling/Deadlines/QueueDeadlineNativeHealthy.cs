using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeHealthy
{
    internal static async Task RunAsync(DatabaseEngine database, QueueLaneRef lane, DateTimeOffset due, long previousReadySequence)
    {
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, QueueDeadlineNativeProtocol.Healthy, QueueDeadlineNativeProtocol.Payload)]);
        var original = QueueDeadlineNativeOperations.Batch(database, command, due, QueueDeadlineNativeProtocol.Root);
        original.Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: (int)QueueDeadlineNativeProtocol.Second);
        var claims = QueueDeadlineNativeOperations.Apply(database, OperationKind.Receive, receive, receive.RequestId,
            due, QueueDeadlineNativeProtocol.Worker).Get<ReceiveResult>();
        await Assert.That(claims.Deliveries.Any(value => value.Id == QueueDeadlineNativeProtocol.Healthy)).IsTrue();
        foreach (var delivery in claims.Deliveries)
        {
            await Assert.That(delivery.PayloadJson).IsEqualTo(QueueDeadlineNativeProtocol.Payload);
            var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
            var result = QueueDeadlineNativeOperations.Apply(database, OperationKind.Delivery, ack, ack.CommandId,
                due, QueueDeadlineNativeProtocol.Worker);
            result.Get<CommitReceipt>();
            await NativeReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineNativeOperations.Apply(database,
                OperationKind.Delivery, ack, ack.CommandId, due, QueueDeadlineNativeProtocol.Worker), result);
            if (delivery.Id == QueueDeadlineNativeProtocol.Healthy)
            {
                var expected = new MessageMetadata(QueueDeadlineNativeProtocol.Healthy, MessageState.Acked,
                    (int)QueueDeadlineNativeProtocol.First, QueueDeadlineNativeProtocol.Third,
                    previousReadySequence + QueueDeadlineNativeProtocol.First, null, null,
                    LeaseVersion: QueueDeadlineNativeProtocol.First);
                var actual = database.InspectMessage(QueueDeadlineNativeProtocol.Root, lane, delivery.Id);
                await Assert.That(actual).IsEqualTo(new MessageInspection(expected, null, null));
            }
        }
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueDeadlineNativeProtocol.CounterSpace, lane.Partition, lane.Queue)))!;
        await Assert.That(counters).IsEqualTo(new QueueCounters(QueueDeadlineNativeProtocol.Initial,
            QueueDeadlineNativeProtocol.Initial, QueueDeadlineNativeProtocol.Initial,
            QueueDeadlineNativeProtocol.Initial, previousReadySequence + QueueDeadlineNativeProtocol.First));
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineNativeOperations.Batch(database,
            command, due, QueueDeadlineNativeProtocol.Root), original);
    }
}
