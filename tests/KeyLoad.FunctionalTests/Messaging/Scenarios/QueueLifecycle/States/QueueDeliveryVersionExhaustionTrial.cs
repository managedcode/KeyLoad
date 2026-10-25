using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeliveryVersionExhaustionTrial
{
    internal static async Task RunAsync(DeliveryAction action, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        var state = await SeedAsync(fixture, action);
        var data = state.Data;
        var domain = QueueClaimVersionExhaustionAssertions.Domain(fixture.Store, data.Lane);
        var before = fixture.Store.Position;
        var failed = fixture.Database.Apply(data.Operation);
        await QueueDeliveryVersionExhaustionAssertions.FailureAsync(failed);
        await Assert.That(fixture.Store.Position).IsEqualTo(before + 1);
        await QueueDeliveryVersionExhaustionAssertions.LeasedAsync(fixture.Database, fixture.Store, state, data.DamagedMetadata);
        await QueueClaimVersionExhaustionAssertions.DomainAsync(fixture.Store, data.Lane, domain);
        await QueueClaimVersionExhaustionAssertions.ReplayAsync(fixture.Database, fixture.Store, data, failed);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = OrleansSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var cold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await QueueDeliveryVersionExhaustionContinuation.RunAsync(QueueWholeFlowStorage.Open(cold), cold,
            fixture.Directory, state, failed, action, identity, token);
    }

    private static async Task<QueueDeliveryVersionExhaustionState> SeedAsync(TestDatabase fixture, DeliveryAction action)
    {
        fixture.Configure(QueueClaimVersionExhaustionProtocol.Queue, ResourceKind.WorkQueue);
        var lane = new QueueLaneRef(fixture.Partition, QueueClaimVersionExhaustionProtocol.Queue);
        var time = QueueWholeFlowStorage.Clock(fixture.Store);
        var message = new EnqueueMessage(lane.Queue, QueueClaimVersionExhaustionProtocol.Message,
            QueueClaimVersionExhaustionProtocol.Payload, QueueClaimVersionExhaustionProtocol.Headers);
        var producer = new CommandRequest(Guid.NewGuid(), lane.Partition, [message]);
        var produced = QueueWholeFlowStorage.Apply(fixture.Database, OperationKind.Batch, producer, producer.CommandId, time);
        produced.Get<CommitReceipt>();
        var ready = new MessageMetadata(message.MessageId, MessageState.Ready, 0, 1, 1, null, null);
        await Assert.That(fixture.Database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, lane, message.MessageId))
            .IsEqualTo(new MessageInspection(ready, message.PayloadJson, message.HeadersJson));
        var key = KeySpace.Partition(QueueClaimVersionExhaustionProtocol.MetadataSpace, lane.Partition, lane.Queue, message.MessageId);
        var row = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        fixture.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, ready with
            {
                StateVersion = long.MaxValue - 1
            });
            return true;
        });
        var request = new ReceiveRequest(Guid.NewGuid(), lane, MaxMessages: 1, LeaseSeconds: QueueClaimVersionExhaustionProtocol.LeaseSeconds);
        var claim = fixture.Database.OwnCommand(QueueLeaseAuthoritySetup.Create(fixture.Database, OperationKind.Receive, request, request.RequestId, time));
        var claimed = fixture.Database.Apply(claim);
        var page = claimed.Get<ReceiveResult>();
        await Assert.That(page.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(page.Token.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(page.Token.AtomicPartitionId).IsEqualTo(lane.Partition.AtomicPartitionId);
        await Assert.That(page.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(page.Deliveries).HasSingleItem();
        await QueueClaimVersionExhaustionAssertions.EqualAsync(delivery, new Delivery(message.MessageId, message.PayloadJson,
            message.HeadersJson, delivery.Token, 1, time.AddSeconds(QueueClaimVersionExhaustionProtocol.LeaseSeconds), 1, 1));
        var repaired = new MessageMetadata(message.MessageId, MessageState.Leased, 1, 2, 1, null, null,
            QueueClaimVersionExhaustionProtocol.Root, 1, delivery.LeaseUntil);
        var maximum = repaired with
        {
            StateVersion = long.MaxValue
        };
        var command = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, action);
        var operation = fixture.Database.OwnCommand(QueueLeaseAuthoritySetup.Create(fixture.Database, OperationKind.Delivery, command,
            command.CommandId, time.AddSeconds(1)));
        var body = new MessageBody(message.MessageId, message.PayloadJson, message.HeadersJson, message.OrderingKey, JsonData.Fingerprint(message));
        var state = new QueueDeliveryVersionExhaustionState(new(lane, time, producer, produced, key, row, maximum, body, operation),
            claim, claimed, delivery, repaired);
        await QueueDeliveryVersionExhaustionAssertions.LeasedAsync(fixture.Database, fixture.Store, state, maximum);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(fixture.Database.Apply(claim), claimed);
        return state;
    }
}
