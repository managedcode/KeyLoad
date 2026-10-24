using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.ResourceExecution;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionReceiveSlotAuthorityAssertions
{
    internal static async Task RefusalAsync(DatabaseEngine db, ZoneTreeStore store,
        ReceiveSubscriptionRequest request, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => SubscriptionReceiveSlotAuthorityFlow.Apply(
                db, OperationKind.ReceiveSubscription, request, request.RequestId, token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo("The subscription delivery window is inconsistent.");
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
            await Assert.That(store.Read(view => view.GetRecord<StoredOutcome>(
                KeySpace.PartitionOutcome(request.Subscription.Source.Partition, "root", request.RequestId)))).IsNull();
        }
    }

    internal static async Task ReplayEmptyAsync(DatabaseEngine db, ZoneTreeStore store,
        ReceiveSubscriptionRequest request, Result<object> original, CancellationToken token)
    {
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        var result = SubscriptionReceiveSlotAuthorityFlow.Apply(db, OperationKind.ReceiveSubscription,
            request, request.RequestId, token);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(result, original);
        await Assert.That(OrleansSerialization.Serialize(result).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, bytes, position, identity);
    }

    internal static async Task SettledAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        ReceiveSubscriptionRequest request, Result<object> result, ReceiveSubscriptionRequest claimRequest,
        Result<object> original, (SubscriptionDeliveryCommand Command, Result<object> Result)[] receipts,
        CancellationToken token)
    {
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        foreach (var receipt in receipts)
        {
            await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, receipt.Command, receipt.Result, token);
        }
        await ReplayEmptyAsync(db, store, request, result, token);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, claimRequest, original, token);
    }

    internal static Task HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        CancellationToken token) => HealthyCoreAsync(db, store, group, null, token);

    internal static Task HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        DateTimeOffset evaluatedAt, CancellationToken token) => HealthyCoreAsync(db, store, group, evaluatedAt, token);

    private static async Task HealthyCoreAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        DateTimeOffset? evaluatedAt, CancellationToken token)
    {
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        ApplyHealthy(db, OperationKind.Batch, publish, publish.CommandId, evaluatedAt, token).Get<CommitReceipt>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 1);
        var result = ApplyHealthy(db, OperationKind.ReceiveSubscription, request, request.RequestId, evaluatedAt, token);
        var delivery = await Assert.That(result.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Event.Position).IsEqualTo(4L);
        await Assert.That(delivery.Event.Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            ApplyHealthy(db, OperationKind.ReceiveSubscription, request, request.RequestId, evaluatedAt, token), result);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        var receipt = ApplyHealthy(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, evaluatedAt, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(receipt, ack, 4);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 4, 4, 4);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, ack, receipt, token);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, request, result, token);
    }

    private static Result<object> ApplyHealthy<T>(DatabaseEngine db, OperationKind kind, T payload, Guid id,
        DateTimeOffset? evaluatedAt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (evaluatedAt is null)
        {
            return SubscriptionReceiveSlotAuthorityFlow.Apply(db, kind, payload, id, token);
        }
        return db.Apply(db.CreateCommand(kind, id, "root", evaluatedAt.Value, System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options)));
    }
}
