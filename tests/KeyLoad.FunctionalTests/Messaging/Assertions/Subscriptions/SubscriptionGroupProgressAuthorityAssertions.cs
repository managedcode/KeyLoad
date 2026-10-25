using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class SubscriptionGroupProgressAuthorityAssertions
{
    private const int RepeatedRefusals = 2;
    private const string CorruptionDetail = "The subscription delivery window is inconsistent.";

    internal static async Task ClaimAsync(ReceiveSubscriptionResult claim, ReceiveSubscriptionRequest request, CommitToken expectedToken)
    {
        await Assert.That(claim.Token).IsEqualTo(expectedToken);
        await Assert.That(claim.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(claim.Deliveries.Select(d => d.Event.Position)).IsEquivalentTo(
            new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await Assert.That(claim.Deliveries.Select(d => d.Event.Data)).IsEquivalentTo(new EventData[]
        {
            new("event1", "Created", "{\"n\":1}"), new("event2", "Created", "{\"n\":2}"),
            new("event3", "Created", "{\"n\":3}")
        }, CollectionOrdering.Matching);
        await Assert.That(claim.Status.Checkpoint).IsEqualTo(0L);
        await Assert.That(claim.Status.IssuedPosition).IsEqualTo(3L);
        foreach (var delivery in claim.Deliveries)
        {
            await Assert.That(delivery.Token).IsNotEmpty();
            await Assert.That(delivery.Attempt).IsEqualTo(1);
            await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        }
    }

    internal static async Task RefuseAsync(DatabaseEngine db, ZoneTreeStore store,
        ReceiveSubscriptionRequest receive, ReceiveSubscriptionRequest cached, ReceiveSubscriptionRequest empty,
        SubscriptionDeliveryCommand ack, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var nack = ack with
        {
            CommandId = Guid.NewGuid(),
            Action = DeliveryAction.Nack
        };
        var renew = ack with
        {
            CommandId = Guid.NewGuid(),
            Action = DeliveryAction.Renew
        };
        for (var attempt = 0; attempt < RepeatedRefusals; attempt++)
        {
            token.ThrowIfCancellationRequested();
            Failure(() => SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription,
                receive, receive.RequestId, token));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
                ack, ack.CommandId, token));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription,
                cached, cached.RequestId, token));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription,
                empty, empty.RequestId, token));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
                nack, nack.CommandId, token));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => SubscriptionCheckpointAuthorityFlow.Apply(db, OperationKind.SubscriptionDelivery,
                renew, renew.CommandId, token));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
            Failure(() => db.GetSubscription("root", receive.Subscription));
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        }
    }

    private static void Failure(Action action)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(action);
        if (error.Code != ErrorCode.Corruption || error.Message != CorruptionDetail)
        {
            throw new InvalidOperationException("The impossible progress did not produce the exact closed corruption refusal.", error);
        }
    }

    internal static CommitToken NextToken(DatabaseEngine db, SubscriptionRef group)
    {
        var placement = db.ReadAtomicPartitionPlacement("root", group.Source.Partition);
        return new(placement.Incarnation, group.Source.Partition.AtomicPartitionId,
            checked(db.LastApplied + 1));
    }

    internal static async Task ReceiptAsync(Result<object> actual, SubscriptionDeliveryCommand request,
        CommitToken expectedToken, long eventPosition)
    {
        var expected = new CommitReceipt(request.CommandId, expectedToken, [new("Ack", request.Subscription.Source.Resource,
                eventPosition.ToString(System.Globalization.CultureInfo.InvariantCulture), 1L)], DurabilityProfile.ProcessDurable, 1);
        await Assert.That(Errors.Code(actual.Problem).Value).IsNull();
        await Assert.That(OrleansSerialization.Serialize(actual.Get<CommitReceipt>()).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
    }
}
