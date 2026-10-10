using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class QueueLifecycleRecoveryContinuation
{
    internal static async Task RunAsync(string directory, DatabaseEngine database, ZoneTreeStore store, ReplicatedOperation original,
        CommitReceipt receipt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var lane = new QueueLaneRef(QueueLifecycleCrashProtocol.Partition, QueueLifecycleCrashProtocol.Queue);
        var request = new ReceiveRequest(Guid.NewGuid(), lane, LeaseSeconds: QueueLifecycleCrashProtocol.LeaseSeconds);
        var claimed = CrashDatabase.Submit(database, OperationKind.Receive, request, request.RequestId).Get<ReceiveResult>();
        var delivery = await Assert.That(claimed.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(QueueLifecycleCrashProtocol.Pending);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueLifecycleCrashProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueLifecycleCrashProtocol.Headers);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(QueueLifecycleCrashProtocol.Two);
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var ackOperation = CrashDatabase.Operation(OperationKind.Delivery, ack, ack.CommandId);
        var acknowledged = database.Apply(ackOperation).Get<CommitReceipt>();
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, database.Apply(original).Get<CommitReceipt>());
        var image = Capture(store, lane);
        store.Dispose();
        using var reopened = new ZoneTreeStore(new(directory), RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
        var cold = QueueLifecycleRecoveryAssertions.Open(reopened);
        await Assert.That(Capture(reopened, lane)).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(receipt, cold.Apply(original).Get<CommitReceipt>());
        await MessagingRecoveryReceiptAssertions.AssertSameNativeValueAsync(acknowledged, cold.Apply(ackOperation).Get<CommitReceipt>());
        var commandId = Guid.NewGuid();
        _ = CrashDatabase.Submit(cold, OperationKind.Batch, new CommandRequest(commandId, lane.Partition,
            [QueueLifecycleCrashSeed.Enqueue(QueueLifecycleCrashProtocol.Healthy)]), commandId).Get<CommitReceipt>();
        var fresh = request with { RequestId = Guid.NewGuid() };
        var healthy = await Assert.That(CrashDatabase.Submit(cold, OperationKind.Receive, fresh, fresh.RequestId).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(healthy.Id).IsEqualTo(QueueLifecycleCrashProtocol.Healthy);
        await Assert.That(healthy.PayloadJson).IsEqualTo(QueueLifecycleCrashProtocol.Payload);
        await Assert.That(healthy.HeadersJson).IsEqualTo(QueueLifecycleCrashProtocol.Headers);
        var completed = ack with { CommandId = Guid.NewGuid(), Token = healthy.Token };
        _ = CrashDatabase.Submit(cold, OperationKind.Delivery, completed, completed.CommandId).Get<CommitReceipt>();
    }

    private static string[] Capture(ZoneTreeStore store, QueueLaneRef lane) => store.Read(view =>
        QueueLifecycleCrashProtocol.Families.SelectMany(family =>
        {
            var page = view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueLifecycleCrashProtocol.ImageRecords);
            if (page.HasMore)
            { throw new InvalidOperationException("The lifecycle recovery image exceeded its bound."); }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
        }).ToArray());
}
