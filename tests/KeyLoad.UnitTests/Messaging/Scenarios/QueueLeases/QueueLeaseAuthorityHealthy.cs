using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLeaseAuthorityHealthy
{
    internal static async Task<QueueLeaseAuthorityHealthyState> RunAsync(DatabaseEngine database,
        ZoneTreeStore store, QueueLeaseAuthorityState state)
    {
        var before = QueueLeaseAuthoritySetup.Counters(store, state.Lane);
        var time = state.Original.EvaluatedAt;
        var producer = new CommandRequest(Guid.NewGuid(), state.Lane.Partition,
            [new EnqueueMessage(state.Lane.Queue, QueueLeaseAuthorityProtocol.Healthy, QueueLeaseAuthorityProtocol.HealthyPayload)]);
        var produced = QueueWholeFlowStorage.Apply(database, OperationKind.Batch, producer, producer.CommandId, time);
        produced.Get<CommitReceipt>();
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, LeaseSeconds: QueueLeaseAuthorityProtocol.LeaseSeconds);
        var claim = QueueLeaseAuthoritySetup.Create(OperationKind.Receive, request, request.RequestId, time);
        var claimed = database.Apply(claim);
        var delivered = await Assert.That(claimed.Get<ReceiveResult>().Deliveries).HasSingleItem();
        await Assert.That(delivered).IsEqualTo(new Delivery(QueueLeaseAuthorityProtocol.Healthy,
            QueueLeaseAuthorityProtocol.HealthyPayload, QueueLeaseAuthorityProtocol.EmptyHeaders, delivered.Token,
            QueueLeaseAuthorityProtocol.One, time.AddSeconds(QueueLeaseAuthorityProtocol.LeaseSeconds),
            QueueLeaseAuthorityProtocol.One, QueueLeaseAuthorityProtocol.One));
        var command = new DeliveryCommand(Guid.NewGuid(), state.Lane, delivered.Token, DeliveryAction.Ack);
        var ack = QueueLeaseAuthoritySetup.Create(OperationKind.Delivery, command, command.CommandId, time);
        var acked = database.Apply(ack);
        var receipt = acked.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations).IsEquivalentTo([new MutationReceipt(nameof(DeliveryAction.Ack),
            state.Lane.Queue, QueueLeaseAuthorityProtocol.Healthy, QueueLeaseAuthorityProtocol.Three)], CollectionOrdering.Matching);
        var counters = before with
        {
            NextReadySequence = QueueLeaseAuthorityProtocol.Two
        };
        var metadata = new MessageMetadata(QueueLeaseAuthorityProtocol.Healthy, MessageState.Acked,
            QueueLeaseAuthorityProtocol.One, QueueLeaseAuthorityProtocol.Three, QueueLeaseAuthorityProtocol.Two,
            null, null, LeaseVersion: QueueLeaseAuthorityProtocol.One);
        var healthy = new QueueLeaseAuthorityHealthyState(producer, produced, claim, claimed, ack, acked, counters, metadata);
        await RequireAsync(database, store, state, healthy);
        return healthy;
    }

    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLeaseAuthorityState state, QueueLeaseAuthorityHealthyState healthy)
    {
        await NativeReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
            healthy.Producer, healthy.Producer.CommandId, state.Original.EvaluatedAt), healthy.Produced);
        await NativeReplayResultAssertions.Same<CommitReceipt>(database.Apply(healthy.Ack), healthy.Acked);
        var stale = database.Apply(healthy.Claim);
        await Assert.That(stale.Error).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(stale.Json).IsNull();
        await Assert.That(stale.NativeValue).IsNull();
        var retained = store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
            state.Lane.Partition, QueueLeaseAuthorityProtocol.Root, healthy.Claim.Id)))!;
        await NativeReplayResultAssertions.Same<ReceiveResult>(retained.Result, healthy.Claimed);
        await RequireAsync(database, store, state, healthy);
    }

    private static async Task RequireAsync(DatabaseEngine database, ZoneTreeStore store,
        QueueLeaseAuthorityState state, QueueLeaseAuthorityHealthyState healthy)
    {
        var actual = database.InspectMessage(QueueLeaseAuthorityProtocol.Root, state.Lane, QueueLeaseAuthorityProtocol.Healthy)!;
        await Assert.That(actual.Metadata).IsEqualTo(healthy.Metadata);
        await Assert.That(actual.PayloadJson).IsNull();
        await Assert.That(actual.HeadersJson).IsNull();
        await Assert.That(QueueLeaseAuthoritySetup.Counters(store, state.Lane)).IsEqualTo(healthy.Counters);
        var body = store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueLeaseAuthorityProtocol.BodySpace,
            state.Lane.Partition, state.Lane.Queue, QueueLeaseAuthorityProtocol.Healthy)));
        await Assert.That(body).IsNull();
    }
}
