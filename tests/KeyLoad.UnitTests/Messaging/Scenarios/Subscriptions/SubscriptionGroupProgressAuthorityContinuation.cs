using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionGroupProgressAuthorityContinuation
{
    internal static async Task RunAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, ReceiveSubscriptionRequest fresh, SubscriptionDeliveryCommand third,
        ReceiveSubscriptionResult original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var empty = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription,
            fresh, fresh.RequestId, token);
        await Assert.That(empty.Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty();
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 0, 3, 3);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(SubscriptionCheckpointAuthorityFlow.Apply(
            db, OperationKind.ReceiveSubscription, fresh, fresh.RequestId, token), empty);
        var expectedThird = SubscriptionGroupProgressAuthorityAssertions.NextToken(db, group);
        var thirdResult = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
            third, third.CommandId, token);
        await SubscriptionGroupProgressAuthorityAssertions.ReceiptAsync(thirdResult, third, expectedThird, 3);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 0, 3, 3);
        foreach (var delivery in original.Deliveries.Take(2))
        {
            var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
            var expectedAck = SubscriptionGroupProgressAuthorityAssertions.NextToken(db, group);
            var result = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
                ack, ack.CommandId, token);
            await SubscriptionGroupProgressAuthorityAssertions.ReceiptAsync(result, ack, expectedAck, delivery.Event.Position);
            await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, ack, result, token);
        }
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        store.Dispose();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var reopened = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(reopened, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(cold, group);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(reopened, cold, third, thirdResult, token);
        await HealthyAsync(reopened, cold, group, token);
    }

    private static async Task HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.Batch, publish, publish.CommandId, token).Get<CommitReceipt>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 1);
        var result = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, token);
        var delivery = await Assert.That(result.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Event.Position).IsEqualTo(4L);
        await Assert.That(delivery.Event.Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(SubscriptionCheckpointAuthorityFlow.Apply(
            db, OperationKind.ReceiveSubscription, request, request.RequestId, token), result);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        var expectedAck = SubscriptionGroupProgressAuthorityAssertions.NextToken(db, group);
        var receipt = SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token);
        await SubscriptionGroupProgressAuthorityAssertions.ReceiptAsync(receipt, ack, expectedAck, 4);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 4, 4, 4);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, ack, receipt, token);
    }
}
