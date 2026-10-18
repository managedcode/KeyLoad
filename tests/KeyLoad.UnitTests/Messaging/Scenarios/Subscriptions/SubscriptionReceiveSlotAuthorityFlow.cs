using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionReceiveSlotAuthorityFlow
{
    internal static async Task RunAsync(GroupDeliveryState completedState, CancellationToken token)
    {
        using var fixture = new TestDatabase();
        fixture.Configure("topic", ResourceKind.Topic);
        SubscriptionTestActions.Publish(fixture);
        var group = SubscriptionTestActions.Configure(fixture, policy: new()
        {
            MaxWindow = 3
        });
        var source = SubscriptionCheckpointAuthorityAssertions.SourceImage(fixture.Store, group.Source.Partition);
        var claimRequest = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        var original = Apply(fixture.Database, OperationKind.ReceiveSubscription, claimRequest, claimRequest.RequestId, token);
        var deliveries = original.Get<ReceiveSubscriptionResult>().Deliveries;
        await Assert.That(deliveries.Select(d => d.Event.Position)).IsEquivalentTo(
            new long[] { 1, 2, 3 }, CollectionOrdering.Matching);
        await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(
            Apply(fixture.Database, OperationKind.ReceiveSubscription, claimRequest, claimRequest.RequestId, token), original);
        var third = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, deliveries[2].Token, DeliveryAction.Ack);
        var thirdResult = Apply(fixture.Database, OperationKind.SubscriptionDelivery, third, third.CommandId, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(thirdResult, third, 3);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(fixture.Database, group, 0, 3, 3);
        var key = SubscriptionCheckpointAuthorityFlow.WindowKey(group, 3);
        var saved = fixture.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The genuine completed receive slot is absent.");
        var originalBytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var originalPosition = fixture.Store.Position;
        var row = NativeSerialization.Deserialize<GroupDelivery>(saved);
        fixture.Store.Commit((tx, _) => { tx.PutRecord(key, row with { Position = 99, State = completedState }); return true; });
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), group, MaxEvents: 3);
        await SubscriptionReceiveSlotAuthorityAssertions.RefusalAsync(fixture.Database, fixture.Store, request, token);
        var bytes = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        fixture.Store.Dispose();
        token.ThrowIfCancellationRequested();
        using var first = new ZoneTreeStore(new(fixture.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var db = QueueWholeFlowStorage.Open(first);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, bytes, position, identity);
        await SubscriptionReceiveSlotAuthorityAssertions.RefusalAsync(db, first, request, token);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(db, first, group.Source, source);
        first.Commit((tx, _) => { tx.Put(key, saved); return true; });
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(first, originalBytes, originalPosition + 2, identity);
        var result = Apply(db, OperationKind.ReceiveSubscription, request, request.RequestId, token);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        await Assert.That(result.Get<ReceiveSubscriptionResult>().Deliveries).IsEmpty();
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 0, 3, 3);
        await SubscriptionReceiveSlotAuthorityAssertions.ReplayEmptyAsync(db, first, request, result, token);
        var firstAck = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, deliveries[0].Token, DeliveryAction.Ack);
        var firstResult = Apply(db, OperationKind.SubscriptionDelivery, firstAck, firstAck.CommandId, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(firstResult, firstAck, 1);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 1, 3, 3);
        var secondAck = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, deliveries[1].Token, DeliveryAction.Ack);
        var secondResult = Apply(db, OperationKind.SubscriptionDelivery, secondAck, secondAck.CommandId, token);
        await SubscriptionCheckpointAuthorityAssertions.ReceiptAsync(secondResult, secondAck, 2);
        await SubscriptionCheckpointAuthorityAssertions.StatusAsync(db, group, 3, 3, 3);
        await ContinueAsync(db, first, fixture.Directory, group, source, request, result, claimRequest, original,
            [(firstAck, firstResult), (secondAck, secondResult), (third, thirdResult)], token);
    }

    private static async Task ContinueAsync(DatabaseEngine db, ZoneTreeStore store, string directory,
        SubscriptionRef group, string[] source, ReceiveSubscriptionRequest request, Result<object> result,
        ReceiveSubscriptionRequest claimRequest, Result<object> original,
        (SubscriptionDeliveryCommand Command, Result<object> Result)[] receipts, CancellationToken token)
    {
        await SubscriptionReceiveSlotAuthorityAssertions.SettledAsync(db, store, group, request, result,
            claimRequest, original, receipts, token);
        var bytes = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        store.Dispose();
        token.ThrowIfCancellationRequested();
        using var second = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var cold = QueueWholeFlowStorage.Open(second);
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(second, bytes, position, identity);
        await SubscriptionCheckpointAuthorityAssertions.SourceAsync(cold, second, group.Source, source);
        await SubscriptionReceiveSlotAuthorityAssertions.SettledAsync(cold, second, group, request, result,
            claimRequest, original, receipts, token);
        await SubscriptionCheckpointAuthorityAssertions.StaleAsync(cold, second,
            receipts[2].Command with
            {
                CommandId = Guid.NewGuid()
            }, token);
        await SubscriptionReceiveSlotAuthorityAssertions.HealthyAsync(cold, second, group, token);
    }

    internal static Result<object> Apply<T>(DatabaseEngine db, OperationKind kind, T payload, Guid id,
        CancellationToken token) => SubscriptionCheckpointAuthorityFlow.Apply(db, kind, payload, id, token);
}
