using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineHealthy
{
    internal static async Task RunAsync(DatabaseEngine database, QueueLaneRef lane, DateTimeOffset due, long previousReadySequence)
    {
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, RuntimeQueueDeadlineProtocol.Healthy, RuntimeQueueDeadlineProtocol.Payload)]);
        var original = QueueDeadlineOperations.Batch(database, command, due, RuntimeQueueDeadlineProtocol.Root);
        original.Get<CommitReceipt>();
        var receive = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: (int)RuntimeQueueDeadlineProtocol.Second);
        var claims = QueueDeadlineOperations.Apply(database, OperationKind.Receive, receive, receive.RequestId,
            due, RuntimeQueueDeadlineProtocol.Worker).Get<ReceiveResult>();
        await Assert.That(claims.Deliveries.Any(value => value.Id == RuntimeQueueDeadlineProtocol.Healthy)).IsTrue();
        foreach (var delivery in claims.Deliveries)
        {
            await Assert.That(delivery.PayloadJson).IsEqualTo(RuntimeQueueDeadlineProtocol.Payload);
            var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
            var result = QueueDeadlineOperations.Apply(database, OperationKind.Delivery, ack, ack.CommandId,
                due, RuntimeQueueDeadlineProtocol.Worker);
            result.Get<CommitReceipt>();
            await ReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineOperations.Apply(database,
                OperationKind.Delivery, ack, ack.CommandId, due, RuntimeQueueDeadlineProtocol.Worker), result);
            if (delivery.Id == RuntimeQueueDeadlineProtocol.Healthy)
            {
                var expected = new MessageMetadata(RuntimeQueueDeadlineProtocol.Healthy, MessageState.Acked,
                    (int)RuntimeQueueDeadlineProtocol.First, RuntimeQueueDeadlineProtocol.Third,
                    previousReadySequence + RuntimeQueueDeadlineProtocol.First, null, null,
                    LeaseVersion: RuntimeQueueDeadlineProtocol.First);
                var actual = database.InspectMessage(RuntimeQueueDeadlineProtocol.Root, lane, delivery.Id);
                await Assert.That(actual).IsEqualTo(new MessageInspection(expected, null, null));
            }
        }
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            RuntimeQueueDeadlineProtocol.CounterSpace, lane.Partition, lane.Queue)))!;
        await Assert.That(counters).IsEqualTo(new QueueCounters(RuntimeQueueDeadlineProtocol.Initial,
            RuntimeQueueDeadlineProtocol.Initial, RuntimeQueueDeadlineProtocol.Initial,
            RuntimeQueueDeadlineProtocol.Initial, previousReadySequence + RuntimeQueueDeadlineProtocol.First));
        await ReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineOperations.Batch(database,
            command, due, RuntimeQueueDeadlineProtocol.Root), original);
    }
}
