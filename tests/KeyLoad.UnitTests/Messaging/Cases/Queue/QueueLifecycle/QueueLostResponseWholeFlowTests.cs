using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLostResponseWholeFlowTests
{
    [Test]
    public Task ExactLeaseExpiryRefusesAckAndRenewBeforeSweepThenColdReclaimFencesOldTokensAndHealthyReceiptReplays()
        => QueueLeaseExpiryEqualityTrial.RunAsync();

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
        await OriginalDeliveryAsync(original, delivery, claimId, time);
        var leased = QueueWholeFlowStorage.Bytes(fixture.Store);
        var leasedPosition = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var database = QueueWholeFlowStorage.Open(reopened);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(reopened, leased, leasedPosition, identity);
        await RefuseChangedContentAsync(database, reopened, OperationKind.Receive, claim with
        {
            LeaseSeconds = 31
        }, claimId, time);
        var retried = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, claim, claimId, time);
        await ReplayResultAssertions.Same<ReceiveResult>(retried, originalResult);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(leased, CollectionOrdering.Matching);
        var ackId = Guid.NewGuid();
        var ack = new DeliveryCommand(ackId, lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, ack, ackId, time);
        acknowledged.Get<CommitReceipt>();
        await Assert.That(database.InspectMessage("root", lane, "input")!.Metadata.State).IsEqualTo(MessageState.Acked);
        var acked = QueueWholeFlowStorage.Bytes(reopened);
        var ackedPosition = reopened.Position;
        reopened.Dispose();
        using var second = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, acked, ackedPosition, identity);
        await RefuseChangedContentAsync(restored, second, OperationKind.Delivery, ack with
        {
            Action = DeliveryAction.Renew
        }, ackId, time);
        var repeated = QueueWholeFlowStorage.Apply(restored, OperationKind.Delivery, ack, ackId, time);
        await ReplayResultAssertions.Same<CommitReceipt>(repeated, acknowledged);
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
        await CompleteHealthyColdAsync(restored, second, fixture.Directory, lane, healthy, ack, acknowledged, time, identity);
    }

    private static async Task RefuseChangedContentAsync<T>(DatabaseEngine database, ZoneTreeStore store,
        OperationKind kind, T changed, Guid originalId, DateTimeOffset time)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var first = QueueWholeFlowStorage.Apply(database, kind, changed, originalId, time);
        await QueueLeaseExpiryEqualityAssertions.RefusalAsync(first, ErrorCode.Conflict);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
        var repeated = QueueWholeFlowStorage.Apply(database, kind, changed, originalId, time);
        await QueueLeaseExpiryEqualityAssertions.RefusalAsync(repeated, ErrorCode.Conflict);
        await Assert.That(OrleansSerialization.Serialize(repeated)).IsEquivalentTo(
            OrleansSerialization.Serialize(first), CollectionOrdering.Matching);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }

    private static async Task CompleteHealthyColdAsync(DatabaseEngine database, ZoneTreeStore store,
        string directory, QueueLaneRef lane, Delivery healthy, DeliveryCommand originalAck,
        Result<object> originalReceipt, DateTimeOffset time, byte[] identity)
    {
        var ack = new DeliveryCommand(Guid.NewGuid(), lane, healthy.Token, DeliveryAction.Ack);
        var receipt = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, ack, ack.CommandId, time);
        receipt.Get<CommitReceipt>();
        await TerminalMessagesAsync(database, lane);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        store.Dispose();
        using var reopened = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var restored = QueueWholeFlowStorage.Open(reopened);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(reopened, bytes, position, identity);
        await ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(restored,
            OperationKind.Delivery, originalAck, originalAck.CommandId, time), originalReceipt);
        await ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(restored,
            OperationKind.Delivery, ack, ack.CommandId, time), receipt);
        await TerminalMessagesAsync(restored, lane);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(reopened, bytes, position, identity);
        var empty = new ReceiveRequest(Guid.NewGuid(), lane);
        await Assert.That(QueueWholeFlowStorage.Apply(restored, OperationKind.Receive, empty, empty.RequestId,
            time).Get<ReceiveResult>().Deliveries).IsEmpty();
        await TerminalMessagesAsync(restored, lane);
    }

    private static async Task TerminalMessagesAsync(DatabaseEngine database, QueueLaneRef lane)
    {
        await Assert.That(database.InspectMessage("root", lane, "input")).IsEqualTo(
            new MessageInspection(new("input", MessageState.Acked, 1, 3, 1, null, null, LeaseVersion: 1), null, null));
        await Assert.That(database.InspectMessage("root", lane, "healthy")).IsEqualTo(
            new MessageInspection(new("healthy", MessageState.Acked, 1, 3, 2, null, null, LeaseVersion: 1), null, null));
    }

    private static async Task OriginalDeliveryAsync(ReceiveResult original, Delivery delivery,
        Guid claimId, DateTimeOffset time)
    {
        await Assert.That(delivery.Id).IsEqualTo("input");
        await Assert.That(delivery.PayloadJson).IsEqualTo("{\"work\":1}");
        await Assert.That(delivery.HeadersJson).IsEqualTo("{\"kind\":\"job\"}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(original.RequestId).IsEqualTo(claimId);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(time.AddSeconds(30));
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
    }
}
