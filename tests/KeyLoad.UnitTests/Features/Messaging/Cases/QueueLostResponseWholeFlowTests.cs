using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLostResponseWholeFlowTests
{
    [Test]
    public async Task AcMsg001ClaimAndAckResponsesReplayExactlyAcrossRealStoreReopen()
    {
        using var fixture = new TestDatabase();
        fixture.Configure("jobs", ResourceKind.WorkQueue);
        fixture.Commit(new EnqueueMessage("jobs", "input", "{\"work\":1}", "{\"kind\":\"job\"}"));
        var time = fixture.Database.EvaluationClock.GetUtcNow();
        var lane = new QueueLaneRef(fixture.Partition, "jobs");
        var claimId = Guid.NewGuid();
        var claim = new ReceiveRequest(claimId, lane, LeaseSeconds: 30);
        var originalResult = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Receive, claim, claimId, time);
        var original = originalResult.Get<ReceiveResult>();
        var delivery = await Assert.That(original.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo("input");
        await Assert.That(delivery.PayloadJson).IsEqualTo("{\"work\":1}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"job\"}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(original.RequestId).IsEqualTo(claimId);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(time.AddSeconds(30));
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        var leased = QueueWholeFlowStorage.Bytes(fixture.Store);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        var retried = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, claim, claimId, time);
        await NativeReplayResultAssertions.Same<ReceiveResult>(retried, originalResult);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(leased, CollectionOrdering.Matching);
        var ackId = Guid.NewGuid();
        var ack = new DeliveryCommand(ackId, lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, ack, ackId, time);
        acknowledged.Get<CommitReceipt>();
        await Assert.That(database.InspectMessage("root", lane, "input")!.Metadata.State).IsEqualTo(MessageState.Acked);
        var acked = QueueWholeFlowStorage.Bytes(reopened);
        reopened.Dispose();
        using var second = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(second);
        var repeated = QueueWholeFlowStorage.Apply(restored, OperationKind.Delivery, ack, ackId, time);
        await NativeReplayResultAssertions.Same<CommitReceipt>(repeated, acknowledged);
        await Assert.That(QueueWholeFlowStorage.Bytes(second)).IsEquivalentTo(acked, CollectionOrdering.Matching);
        var createId = Guid.NewGuid();
        QueueWholeFlowStorage.Apply(restored, OperationKind.Batch, new CommandRequest(createId, fixture.Partition,
            [new EnqueueMessage("jobs", "healthy", "{\"work\":2}")]), createId, time).Get<CommitReceipt>();
        var healthyId = Guid.NewGuid();
        var healthy = await Assert.That(QueueWholeFlowStorage.Apply(restored, OperationKind.Receive,
            new ReceiveRequest(healthyId, lane), healthyId, time).Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(healthy.Id).IsEqualTo("healthy");
        await Assert.That(healthy.PayloadJson).IsEqualTo("{\"work\":2}");
        await Assert.That(healthy.Attempt).IsEqualTo(1);
        await Assert.That(restored.InspectMessage("root", lane, "input")!.Metadata.State).IsEqualTo(MessageState.Acked);
    }
}
