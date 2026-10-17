using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineColdHealthyContinuation
{
    internal static async Task RunAsync(ZoneTreeStore store, string directory, QueueLaneRef lane,
        DeliveryCommand ack, OperationResult acked, CommandRequest producer, OperationResult produced,
        MessageInspection terminal, DateTimeOffset due, byte[] identity, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, bytes, position, identity);
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineNativeOperations.Apply(database,
            OperationKind.Delivery, ack, ack.CommandId, due), acked);
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueDeadlineNativeOperations.Batch(database,
            producer, due, "root"), produced);
        await Assert.That(database.InspectMessage("root", lane, "input")).IsEqualTo(terminal);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, bytes, position, identity);
        var healthy = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, "healthy", "{\"work\":2}", "{\"kind\":\"healthy\"}")]);
        QueueDeadlineNativeOperations.Batch(database, healthy, due, "root").Get<CommitReceipt>();
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var delivery = await Assert.That(QueueDeadlineNativeOperations.Apply(database, OperationKind.Receive,
            request, request.RequestId, due).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo("healthy");
        await Assert.That(delivery.PayloadJson).IsEqualTo("{\"work\":2}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"healthy\"}");
        var finalAck = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        QueueDeadlineNativeOperations.Apply(database, OperationKind.Delivery, finalAck, finalAck.CommandId, due).Get<CommitReceipt>();
        await Assert.That(database.InspectMessage("root", lane, "input")).IsEqualTo(terminal);
        await Assert.That(database.InspectMessage("root", lane, "healthy")).IsEqualTo(new MessageInspection(
            new("healthy", MessageState.Acked, 1, 3, terminal.Metadata.ReadySequence + 1, null, null, LeaseVersion: 1), null, null));
    }
}
