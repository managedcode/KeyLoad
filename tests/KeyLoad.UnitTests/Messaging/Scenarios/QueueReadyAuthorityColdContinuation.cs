using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueReadyAuthorityColdContinuation
{
    internal static async Task RunAsync(global::KeyLoad.Core.DatabaseEngine database,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, string directory, QueueLaneRef lane,
        ReceiveResult original, bool expired, Command<DatabaseCommand> operation, CommandRequest producer,
        Result<object> produced, byte[] identity, CancellationToken token)
    {
        var receipts = new List<(DeliveryCommand Command, Result<object> Receipt)>();
        if (expired)
        {
            await Assert.That(original.Deliveries).IsEmpty();
        }
        else
        {
            var delivery = await Assert.That(original.Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo("input");
            await Assert.That(delivery.PayloadJson).IsEqualTo("{\"input\":1}");
            await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"job\"}");
            receipts.Add(AckCounter(database, lane, delivery, new DateTimeOffset(operation.Timestamp)));
        }
        var healthy = new CommandRequest(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage("jobs", "healthy", "{\"input\":2}", "{\"kind\":\"healthy\"}")]);
        QueueWholeFlowStorage.Apply(database, OperationKind.Batch, healthy, healthy.CommandId, new DateTimeOffset(operation.Timestamp)).Get<CommitReceipt>();
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var current = await Assert.That(QueueWholeFlowStorage.Apply(database, OperationKind.Receive, request,
            request.RequestId, new DateTimeOffset(operation.Timestamp)).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(current.Id).IsEqualTo("healthy");
        await Assert.That(current.PayloadJson).IsEqualTo("{\"input\":2}");
        await Assert.That(current.HeadersJson).IsEqualTo("{\"kind\":\"healthy\"}");
        receipts.Add(AckCounter(database, lane, current, new DateTimeOffset(operation.Timestamp)));
        await Assert.That(database.InspectMessage("root", lane, "healthy")).IsEqualTo(new MessageInspection(
            new("healthy", MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), null, null));
        var input = database.InspectMessage("root", lane, "input")!;
        await Assert.That(input).IsEqualTo(new MessageInspection(new("input",
            expired ? MessageState.Expired : MessageState.Acked, expired ? 0 : 1, expired ? 2 : 3,
            1, null, expired ? new DateTimeOffset(operation.Timestamp).AddSeconds(-1) : null, LeaseVersion: expired ? 0 : 1), null, null));
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new global::KeyLoad.Storage.ZoneTree.ZoneTreeStore(new(directory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, bytes, position, identity);
        foreach (var (command, result) in receipts)
        {
            await global::KeyLoad.UnitTests.ResourceExecution.ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(restored,
                OperationKind.Delivery, command, command.CommandId, new DateTimeOffset(operation.Timestamp)), result);
        }
        await global::KeyLoad.UnitTests.ResourceExecution.ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(restored,
            OperationKind.Batch, producer, producer.CommandId, new DateTimeOffset(operation.Timestamp)), produced);
        await Assert.That(restored.InspectMessage("root", lane, "input")).IsEqualTo(input);
        await Assert.That(restored.InspectMessage("root", lane, "healthy")).IsEqualTo(new MessageInspection(
            new("healthy", MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), null, null));
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, bytes, position, identity);
        var counters = second.Read(view => view.GetRecord<QueueCounters>(
            global::KeyLoad.Core.KeySpace.Partition("queue-counters", lane.Partition, lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(0, 0, 0, 0, 2));
    }

    private static (DeliveryCommand Command, Result<object> Receipt) AckCounter(
        global::KeyLoad.Core.DatabaseEngine database, QueueLaneRef lane, Delivery delivery, DateTimeOffset time)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var result = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, command, command.CommandId, time);
        result.Get<CommitReceipt>();
        return (command, result);
    }
}
