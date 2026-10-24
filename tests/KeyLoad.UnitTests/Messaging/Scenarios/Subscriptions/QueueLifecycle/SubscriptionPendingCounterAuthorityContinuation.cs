using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Messaging.SubscriptionContinuationProtocol;

namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionPendingCounterAuthorityContinuation
{
    internal static async Task RunAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, ReceiveSubscriptionResult original, ReceiveSubscriptionRequest request,
        DateTimeOffset due, List<SubscriptionContinuationOutcome> retained, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, store, group.Source,
            SubscriptionCheckpointAuthorityAssertions.SourceImage(store, group.Source.Partition));
        var expected = SubscriptionGroupProgressAuthorityAssertions.NextToken(db, group);
        var result = QueueWholeFlowStorage.Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, due);
        await SubscriptionContinuationAssertions.ClaimAsync(db, store, group, request, result,
            original.Deliveries[FirstDelivery].Event, ReclaimedAttempt, ReclaimedVersion, due,
            InitialCheckpoint, OriginalTail, OriginalTail, expected);
        await SubscriptionContinuationAssertions.ReplayAsync(db, store,
            new(OperationKind.ReceiveSubscription, request, request.RequestId, result), due, token);
        retained.Add(new(OperationKind.ReceiveSubscription, request, request.RequestId, result));
        var initial = new ReceiveSubscriptionRequest(original.RequestId, group, MaxEvents: Window);
        retained.Add(new(OperationKind.ReceiveSubscription, initial, initial.RequestId,
            Result<object>.Succeed(original)));
        var delivery = result.Get<ReceiveSubscriptionResult>().Deliveries.Single();
        var remaining = new List<SubscriptionDelivery>
        {
            delivery, original.Deliveries[SecondDelivery], original.Deliveries[ThirdDelivery]
        };
        await SubscriptionContinuationAssertions.WindowAsync(store, group, InitialCheckpoint, OriginalTail, remaining);
        foreach (var current in new[] { delivery, original.Deliveries[SecondDelivery], original.Deliveries[ThirdDelivery] })
        {
            await CompleteAsync(db, store, group, current, due, retained, token);
            remaining.Remove(current);
            await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group,
                current.Event.Position, OriginalTail, OriginalTail);
            await SubscriptionContinuationAssertions.WindowAsync(store, group, current.Event.Position, OriginalTail, remaining);
        }
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, OriginalTail, OriginalTail, OriginalTail);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        await HealthyAsync(db, store, group, due, retained, token);
        await ColdAsync(store, directory, group, original, due, retained, token);
    }

    private static async Task CompleteAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        SubscriptionDelivery current, DateTimeOffset time, List<SubscriptionContinuationOutcome> retained,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, current.Token, DeliveryAction.Ack);
        var expected = SubscriptionGroupProgressAuthorityAssertions.NextToken(db, group);
        var completed = QueueWholeFlowStorage.Apply(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, time);
        await SubscriptionContinuationAssertions.ReceiptAsync(completed, ack.CommandId, expected,
            new(nameof(DeliveryAction.Ack), group.Source.Resource,
                current.Event.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), current.LeaseVersion));
        var entry = new SubscriptionContinuationOutcome(OperationKind.SubscriptionDelivery, ack, ack.CommandId, completed);
        retained.Add(entry);
        await SubscriptionContinuationAssertions.ReplayAsync(db, store, entry, time, token);
    }

    private static async Task HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group,
        DateTimeOffset time, List<SubscriptionContinuationOutcome> retained, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic(Topic, [new(HealthyId, EventType, HealthyPayload)])]);
        var expected = SubscriptionGroupProgressAuthorityAssertions.NextToken(db, group);
        var published = QueueWholeFlowStorage.Apply(db, OperationKind.Batch, publish, publish.CommandId, time);
        await SubscriptionContinuationAssertions.ReceiptAsync(published, publish.CommandId, expected,
            new(PublishMutation, Topic, HealthyId, HealthyPosition));
        var entry = new SubscriptionContinuationOutcome(OperationKind.Batch, publish, publish.CommandId, published);
        retained.Add(entry);
        await SubscriptionContinuationAssertions.ReplayAsync(db, store, entry, time, token);
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: SingleEventCount);
        expected = SubscriptionGroupProgressAuthorityAssertions.NextToken(db, group);
        var claimed = QueueWholeFlowStorage.Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, time);
        await SubscriptionContinuationAssertions.ClaimAsync(db, store, group, request, claimed,
            new(group.Source, HealthyPosition, HealthyPosition, new(HealthyId, EventType, HealthyPayload), time),
            InitialAttempt, InitialLeaseVersion, time, OriginalTail, HealthyPosition, HealthyPosition, expected);
        await SubscriptionContinuationAssertions.ReplayAsync(db, store,
            new(OperationKind.ReceiveSubscription, request, request.RequestId, claimed), time, token);
        retained.Add(new(OperationKind.ReceiveSubscription, request, request.RequestId, claimed));
        var delivery = claimed.Get<ReceiveSubscriptionResult>().Deliveries.Single();
        await SubscriptionContinuationAssertions.WindowAsync(store, group, OriginalTail, HealthyPosition, [delivery]);
        await CompleteAsync(db, store, group, delivery, time, retained, token);
    }

    private static async Task ColdAsync(ZoneTreeStore store, string directory, SubscriptionRef group,
        ReceiveSubscriptionResult original, DateTimeOffset time, List<SubscriptionContinuationOutcome> retained,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = OrleansSerialization.Serialize(store.Identity);
        store.Dispose();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(cold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(cold, image, position, identity);
        await SubscriptionContinuationAssertions.TerminalAsync(db, cold, group, original, time);
        foreach (var entry in retained)
        {
            if (entry.Result.Value is ReceiveSubscriptionResult { Deliveries.IsEmpty: false })
            {
                await SubscriptionContinuationAssertions.SettledAsync(db, cold, entry, time, token);
            }
            else
            {
                await SubscriptionContinuationAssertions.ReplayAsync(db, cold, entry, time, token);
            }
        }
        await SubscriptionContinuationAssertions.TerminalAsync(db, cold, group, original, time);
    }
}
