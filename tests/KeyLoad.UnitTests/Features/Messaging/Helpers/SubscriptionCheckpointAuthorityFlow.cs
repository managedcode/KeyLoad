using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionCheckpointAuthorityFlow
{
    internal static async Task RunAsync(GroupDeliveryState completedState, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("topic", ResourceKind.Topic);
        SubscriptionTestActions.Publish(fixture);
        var group = SubscriptionTestActions.Configure(fixture, policy: new() { MaxWindow = 3 });
        var source = SubscriptionCheckpointAuthorityAssertions.SourceImage(fixture.Store, group.Source.Partition);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(fixture.Database, fixture.Store, group.Source, source);
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        var original = Apply(fixture.Database, OperationKind.ReceiveSubscription, request, request.RequestId, token);
        var claim = original.Get<ReceiveSubscriptionResult>();
        await Assert.That(claim.Deliveries.Select(d => d.Event.Position)).IsEquivalentTo(
            new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            Apply(fixture.Database, OperationKind.ReceiveSubscription, request, request.RequestId, token), original);
        var third = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, claim.Deliveries[2].Token, DeliveryAction.Ack);
        var thirdResult = Apply(fixture.Database, OperationKind.SubscriptionDelivery, third, third.CommandId, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(thirdResult, third, 3);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(fixture.Database, group, 0, 3, 3);
        var first = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, claim.Deliveries[0].Token, DeliveryAction.Ack);
        var firstResult = Apply(fixture.Database, OperationKind.SubscriptionDelivery, first, first.CommandId, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(firstResult, first, 1);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(fixture.Database, group, 1, 3, 3);
        var second = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, claim.Deliveries[1].Token, DeliveryAction.Ack);
        var key = WindowKey(group, 3);
        var saved = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The genuine acknowledged gap row is missing.");
        var row = NativeSerialization.Deserialize<GroupDelivery>(saved);
        fixture.Store.Commit((tx, _) => { tx.PutRecord(key, row with { Position = 99, State = completedState }); return true; });
        await SubscriptionCheckpointAuthorityAssertions.CorruptionAsync(fixture.Database, fixture.Store, second, token);
        var cut = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        using var firstCold = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(firstCold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(firstCold, cut, position, identity);
        await SubscriptionCheckpointAuthorityAssertions.CorruptionAsync(db, firstCold, second, token);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 1, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, firstCold, group.Source, source);
        await ContinueAsync(db, firstCold, fixture.Directory, group, second, first, firstResult,
            third, thirdResult, request, original, saved, key, identity, source, token);
    }

    private static async Task ContinueAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, SubscriptionDeliveryCommand second, SubscriptionDeliveryCommand first,
        OperationResult firstResult, SubscriptionDeliveryCommand third, OperationResult thirdResult,
        ReceiveSubscriptionRequest request, OperationResult original, byte[] saved, byte[] key,
        byte[] identity, string[] source, CancellationToken token)
    {
        store.Commit((tx, _) => { tx.Put(key, saved); return true; });
        var secondResult = Apply(db, OperationKind.SubscriptionDelivery, second, second.CommandId, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(secondResult, second, 2);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, first, firstResult, token);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, second, secondResult, token);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, third, thirdResult, token);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, request, original, token);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        store.Dispose();
        using var secondCold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(secondCold);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(secondCold, bytes, position, identity);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(cold, group, 3, 3, 3);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(secondCold, group);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(cold, secondCold, group.Source, source);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(cold, secondCold, first, firstResult, token);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(cold, secondCold, second, secondResult, token);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(cold, secondCold, third, thirdResult, token);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(cold, secondCold, request, original, token);
        var stale = third with { CommandId = Guid.NewGuid() };
        await SubscriptionCheckpointAuthorityAssertions.StaleAsync(cold, secondCold, stale, token);
        await HealthyAsync(cold, secondCold, group, token);
    }

    private static async Task HealthyAsync(DatabaseEngine db, ZoneTreeStore store, SubscriptionRef group, CancellationToken token)
    {
        var publish = new CommandRequest(Guid.NewGuid(), group.Source.Partition,
            [new PublishTopic("topic", [new("event4", "Created", "{\"n\":4}")])]);
        Apply(db, OperationKind.Batch, publish, publish.CommandId, token).Get<CommitReceipt>();
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 1);
        var result = Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, token);
        var delivery = await Assert.That(result.Get<ReceiveSubscriptionResult>().Deliveries).HasSingleItem();
        await Assert.That(delivery.Event.Position).IsEqualTo(4L);
        await Assert.That(delivery.Event.Data).IsEqualTo(new EventData("event4", "Created", "{\"n\":4}"));
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, token), result);
        var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, delivery.Token, DeliveryAction.Ack);
        var receipt = Apply(db, OperationKind.SubscriptionDelivery, ack, ack.CommandId, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(receipt, ack, 4);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 4, 4, 4);
        await SubscriptionCheckpointAuthorityAssertions.NoWindowAsync(store, group);
        await SubscriptionCheckpointAuthorityAssertions.ReplayAsync(db, store, ack, receipt, token);
        await SubscriptionCheckpointAuthorityAssertions.SettledClaimAsync(db, store, request, result, token);
    }

    internal static OperationResult Apply<T>(DatabaseEngine db, OperationKind kind, T payload, Guid id, CancellationToken token)
        => SubscriptionFilterColdTrial.Apply(db, kind, payload, id, token);

    internal static byte[] WindowKey(SubscriptionRef group, long? position)
        => position is null
            ? KeySpace.Partition("subscription-window", group.Source.Partition, group.Source.Resource,
                group.Source.Kind.ToString(), group.Source.StreamId, group.Source.Generation, group.GroupId, 1L)
            : KeySpace.Partition("subscription-window", group.Source.Partition, group.Source.Resource,
                group.Source.Kind.ToString(), group.Source.StreamId, group.Source.Generation, group.GroupId, 1L, position.Value);
}
