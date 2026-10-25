using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueAccountingFaultHealthy
{
    internal static async Task<QueueAccountingHealthyState> RunAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueAccountingFaultState state)
    {
        var prior = QueueAccountingFaultSetup.Counters(store, state.Lane);
        var producer = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new EnqueueMessage(state.Lane.Queue, QueueAccountingFaultProtocol.Healthy, QueueAccountingFaultProtocol.HealthyPayload)]);
        var produced = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, producer, producer.CommandId, new DateTimeOffset(state.Operation.Timestamp));
        produced.Get<CommitReceipt>();
        var claim = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueAccountingFaultProtocol.LeaseSeconds);
        var claimed = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, claim, claim.RequestId, new DateTimeOffset(state.Operation.Timestamp));
        var delivery = await Assert.That(claimed.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(QueueAccountingFaultProtocol.Healthy);
        await Assert.That(delivery.PayloadJson).IsEqualTo(QueueAccountingFaultProtocol.HealthyPayload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(QueueAccountingFaultProtocol.EmptyHeaders);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivery.Token, DeliveryAction.Ack);
        var acked = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, ack, ack.CommandId, new DateTimeOffset(state.Operation.Timestamp));
        var receipt = acked.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(ack.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo([new MutationReceipt(nameof(DeliveryAction.Ack), state.Lane.Queue,
            QueueAccountingFaultProtocol.Healthy, QueueAccountingFaultProtocol.Three)], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var counters = prior with
        {
            NextReadySequence = prior.NextReadySequence + QueueAccountingFaultProtocol.One
        };
        var metadata = new MessageMetadata(QueueAccountingFaultProtocol.Healthy, MessageState.Acked,
            QueueAccountingFaultProtocol.One, QueueAccountingFaultProtocol.Three, counters.NextReadySequence, null, null,
            LeaseVersion: QueueAccountingFaultProtocol.One);
        var healthy = new QueueAccountingHealthyState(producer, produced, claim, claimed, ack, acked, counters, metadata);
        await AssertHealthyAsync(database, state, healthy);
        return healthy;
    }

    internal static async Task ReplayAsync(DatabaseEngine database, QueueAccountingFaultState state, QueueAccountingHealthyState healthy)
    {
        await ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
            healthy.Producer, healthy.Producer.CommandId, new DateTimeOffset(state.Operation.Timestamp)), healthy.Produced);
        await ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database, OperationKind.Delivery,
            healthy.Ack, healthy.Ack.CommandId, new DateTimeOffset(state.Operation.Timestamp)), healthy.Acked);
        var stale = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, healthy.Claim, healthy.Claim.RequestId, new DateTimeOffset(state.Operation.Timestamp));
        await Assert.That(Errors.Code(stale.Problem).Value).IsEqualTo(ErrorCode.StaleLease);

        await Assert.That(stale.Value).IsNull();
        var retained = database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Lane.Partition, QueueAccountingFaultProtocol.Root, healthy.Claim.RequestId)))!;
        await ReplayResultAssertions.Same<ReceiveResult>(retained.Result, healthy.Claimed);
        await AssertHealthyAsync(database, state, healthy);
    }

    private static async Task AssertHealthyAsync(DatabaseEngine database, QueueAccountingFaultState state, QueueAccountingHealthyState healthy)
    {
        var inspection = database.InspectMessage(QueueAccountingFaultProtocol.Root, state.Lane, QueueAccountingFaultProtocol.Healthy)!;
        await Assert.That(inspection.Metadata).IsEqualTo(healthy.Metadata);
        await Assert.That(inspection.PayloadJson).IsNull();
        await Assert.That(inspection.HeadersJson).IsNull();
        await Assert.That(QueueAccountingFaultSetup.Counters((ZoneTreeStore)database.Store, state.Lane)).IsEqualTo(healthy.Counters);
        var body = database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
            QueueAccountingFaultProtocol.BodySpace, state.Lane.Partition, state.Lane.Queue, QueueAccountingFaultProtocol.Healthy)));
        await Assert.That(body).IsNull();
    }
}
